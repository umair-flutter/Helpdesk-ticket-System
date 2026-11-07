using System.ComponentModel.DataAnnotations;
using Help_DeskTicket_System.Data;
using Help_DeskTicket_System.Models;
using Help_DeskTicket_System.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Help_DeskTicket_System.Pages.Kb;

public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int? CategoryId { get; set; }
    public List<KbArticle> Articles { get; set; } = [];
    public List<Category> Categories { get; set; } = [];

    private IQueryable<KbArticle> Visible() =>
        User.IsStaff() ? db.KbArticles : db.KbArticles.Where(a => a.IsPublished);

    public async Task OnGetAsync()
    {
        var q = Visible().Include(a => a.Category).AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q)) q = q.Where(a => a.Title.Contains(Q) || a.Body.Contains(Q));
        if (CategoryId.HasValue) q = q.Where(a => a.CategoryId == CategoryId);
        Articles = await q.OrderByDescending(a => a.Views).ThenBy(a => a.Title).ToListAsync();
        var used = await Visible().Where(a => a.CategoryId != null).Select(a => a.CategoryId).Distinct().ToListAsync();
        Categories = await db.Categories.Where(c => used.Contains(c.Id)).OrderBy(c => c.Name).ToListAsync();
    }

    /// <summary>Used by the New Ticket form to suggest answers while the user types.</summary>
    public async Task<IActionResult> OnGetSuggestAsync(string q)
    {
        var words = (q ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2).Take(5).ToList();
        if (words.Count == 0) return new JsonResult(Array.Empty<object>());
        var candidates = await db.KbArticles.Where(a => a.IsPublished).Select(a => new { a.Id, a.Title, a.Body }).ToListAsync();
        var hits = candidates
            .Select(a => new { a.Id, a.Title, Score = words.Count(w => a.Title.Contains(w, StringComparison.OrdinalIgnoreCase)) * 3
                                                    + words.Count(w => a.Body.Contains(w, StringComparison.OrdinalIgnoreCase)) })
            .Where(a => a.Score > 0).OrderByDescending(a => a.Score).Take(5)
            .Select(a => new { id = a.Id, title = a.Title });
        return new JsonResult(hits);
    }
}

public class ArticleModel(AppDbContext db) : PageModel
{
    public KbArticle Article { get; set; } = null!;
    public List<KbArticle> Related { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var a = await db.KbArticles.Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id);
        if (a is null || (!a.IsPublished && !User.IsStaff())) return NotFound();
        a.Views++;
        await db.SaveChangesAsync();
        Article = a;
        Related = await db.KbArticles.Where(x => x.IsPublished && x.Id != id && x.CategoryId == a.CategoryId && a.CategoryId != null).Take(5).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostVoteAsync(int id, bool helpful)
    {
        var key = $"kbvote-{id}";
        if (Request.Cookies.ContainsKey(key)) return RedirectToPage(new { id });
        var a = await db.KbArticles.FindAsync(id);
        if (a is null) return NotFound();
        if (helpful) a.HelpfulYes++; else a.HelpfulNo++;
        await db.SaveChangesAsync();
        Response.Cookies.Append(key, "1", new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), HttpOnly = true });
        TempData["voted"] = true;
        return RedirectToPage(new { id });
    }
}

public class EditModel(AppDbContext db) : PageModel
{
    public class Input
    {
        [Required(ErrorMessage = V.Required), MaxLength(200, ErrorMessage = V.MaxLength), Display(Name = "Title")] public string Title { get; set; } = "";
        [Required(ErrorMessage = V.Required), Display(Name = "Content")] public string Body { get; set; } = "";
        public int? CategoryId { get; set; }
        public bool IsPublished { get; set; } = true;
    }
    [BindProperty] public Input Form { get; set; } = new();
    public int? Id { get; set; }
    public List<Category> Categories { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Id = id;
        Categories = await db.Categories.OrderBy(c => c.Name).ToListAsync();
        if (id is null) return Page();
        var a = await db.KbArticles.FindAsync(id);
        if (a is null) return NotFound();
        Form = new Input { Title = a.Title, Body = a.Body, CategoryId = a.CategoryId, IsPublished = a.IsPublished };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (!ModelState.IsValid) { Id = id; Categories = await db.Categories.OrderBy(c => c.Name).ToListAsync(); return Page(); }
        var a = id is null ? new KbArticle() : await db.KbArticles.FindAsync(id);
        if (a is null) return NotFound();
        a.Title = Form.Title; a.Body = Form.Body; a.CategoryId = Form.CategoryId; a.IsPublished = Form.IsPublished; a.UpdatedAt = DateTime.UtcNow;
        if (id is null) db.KbArticles.Add(a);
        await db.SaveChangesAsync();
        TempData["ok"] = Lang.T("Article saved.");
        return RedirectToPage("Article", new { id = a.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await db.KbArticles.Where(a => a.Id == id).ExecuteDeleteAsync();
        TempData["ok"] = Lang.T("Article deleted.");
        return RedirectToPage("Index");
    }
}
