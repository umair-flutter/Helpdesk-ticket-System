using Help_DeskTicket_System.Data;
using Help_DeskTicket_System.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Help_DeskTicket_System.Pages.Admin;

/// <summary>Admin-managed lists: departments, categories, priorities (with SLA hours) and ticket statuses.</summary>
public class LookupsModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "statuses";
    public List<Department> Departments { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
    public List<Priority> Priorities { get; set; } = [];
    public List<TicketStatus> Statuses { get; set; } = [];

    public async Task OnGetAsync()
    {
        Departments = await db.Departments.OrderBy(d => d.Name).ToListAsync();
        Categories = await db.Categories.Include(c => c.Department).OrderBy(c => c.Name).ToListAsync();
        Priorities = await db.Priorities.OrderBy(p => p.SortOrder).ToListAsync();
        Statuses = await db.Statuses.OrderBy(s => s.SortOrder).ToListAsync();
    }

    private IActionResult Done(string tab, string msg = "Saved.") { TempData["ok"] = Lang.T(msg); return RedirectToPage(new { tab }); }
    private IActionResult Fail(string tab, string msg) { TempData["err"] = Lang.T(msg); return RedirectToPage(new { tab }); }

    public async Task<IActionResult> OnPostDepartmentAsync(Department d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return Fail("departments", "Name is required.");
        if (d.Id == 0) db.Departments.Add(d); else db.Departments.Update(d);
        await db.SaveChangesAsync();
        return Done("departments");
    }

    public async Task<IActionResult> OnPostCategoryAsync(Category c)
    {
        if (string.IsNullOrWhiteSpace(c.Name)) return Fail("categories", "Name is required.");
        if (c.Id == 0) db.Categories.Add(c); else db.Categories.Update(c);
        await db.SaveChangesAsync();
        return Done("categories");
    }

    public async Task<IActionResult> OnPostPriorityAsync(Priority p)
    {
        if (string.IsNullOrWhiteSpace(p.Name)) return Fail("priorities", "Name is required.");
        if (p.SlaResponseHours <= 0 || p.SlaResolveHours <= 0) return Fail("priorities", "SLA hours must be positive.");
        if (p.IsDefault) await db.Priorities.Where(x => x.Id != p.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false));
        if (p.Id == 0) db.Priorities.Add(p); else db.Priorities.Update(p);
        await db.SaveChangesAsync();
        return Done("priorities");
    }

    public async Task<IActionResult> OnPostStatusAsync(TicketStatus s)
    {
        if (string.IsNullOrWhiteSpace(s.Name)) return Fail("statuses", "Name is required.");
        if (s.IsDefault && s.IsClosed) return Fail("statuses", "The default status for new tickets can't be a closed status.");
        if (s.IsDefault) await db.Statuses.Where(x => x.Id != s.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsDefault, false));
        if (s.Id == 0) db.Statuses.Add(s); else db.Statuses.Update(s);
        await db.SaveChangesAsync();
        if (!await db.Statuses.AnyAsync(x => !x.IsClosed)) return Fail("statuses", "Warning: you need at least one open (not closed) status.");
        if (!await db.Statuses.AnyAsync(x => x.IsClosed)) return Fail("statuses", "Warning: you need at least one closed status.");
        return Done("statuses");
    }

    public async Task<IActionResult> OnPostDeleteAsync(string kind, int id)
    {
        switch (kind)
        {
            case "department":
                await db.Departments.Where(x => x.Id == id).ExecuteDeleteAsync();
                return Done("departments", "Deleted.");
            case "category":
                await db.Categories.Where(x => x.Id == id).ExecuteDeleteAsync();
                return Done("categories", "Deleted.");
            case "priority":
                if (await db.Tickets.AnyAsync(t => t.PriorityId == id)) return Fail("priorities", "This priority is used by tickets and can't be deleted.");
                if (await db.Priorities.CountAsync() <= 1) return Fail("priorities", "Keep at least one priority.");
                await db.Priorities.Where(x => x.Id == id).ExecuteDeleteAsync();
                return Done("priorities", "Deleted.");
            case "status":
                if (await db.Tickets.AnyAsync(t => t.StatusId == id)) return Fail("statuses", "This status is used by tickets and can't be deleted.");
                if (await db.Statuses.CountAsync() <= 2) return Fail("statuses", "Keep at least one open and one closed status.");
                await db.Statuses.Where(x => x.Id == id).ExecuteDeleteAsync();
                return Done("statuses", "Deleted.");
        }
        return BadRequest();
    }
}
