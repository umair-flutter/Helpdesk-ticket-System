using Help_DeskTicket_System.Data;
using Help_DeskTicket_System.Models;
using Help_DeskTicket_System.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Help_DeskTicket_System.Pages.Notifications;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<Notification> Items { get; set; } = [];

    private IQueryable<Notification> Mine() => db.Notifications.Where(n => n.UserId == User.Id());

    public async Task OnGetAsync() =>
        Items = await Mine().OrderByDescending(n => n.CreatedAt).Take(200).ToListAsync();

    /// <summary>JSON for the bell dropdown.</summary>
    public async Task<IActionResult> OnGetRecentAsync()
    {
        var unread = await Mine().CountAsync(n => !n.IsRead);
        var items = await Mine().OrderByDescending(n => n.CreatedAt).Take(10).ToListAsync();
        return new JsonResult(new { unread, items = items.Select(n => new { n.Id, n.Message, n.IsRead, ago = Ui.Ago(n.CreatedAt) }) });
    }

    public async Task<IActionResult> OnGetOpenAsync(int id)
    {
        var n = await Mine().FirstOrDefaultAsync(x => x.Id == id);
        if (n is null) return RedirectToPage();
        n.IsRead = true;
        await db.SaveChangesAsync();
        return LocalRedirect(n.Link is { } l && Url.IsLocalUrl(l) ? l : "/Notifications");
    }

    public async Task<IActionResult> OnPostReadAllAsync()
    {
        await Mine().Where(n => !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostClearAsync()
    {
        await Mine().Where(n => n.IsRead).ExecuteDeleteAsync();
        return RedirectToPage();
    }
}
