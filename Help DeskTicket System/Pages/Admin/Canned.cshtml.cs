using Help_DeskTicket_System.Data;
using Help_DeskTicket_System.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Help_DeskTicket_System.Pages.Admin;

public class CannedModel(AppDbContext db) : PageModel
{
    public List<CannedResponse> Items { get; set; } = [];

    public async Task OnGetAsync() => Items = await db.CannedResponses.OrderBy(c => c.Title).ToListAsync();

    public async Task<IActionResult> OnPostSaveAsync(CannedResponse c)
    {
        if (string.IsNullOrWhiteSpace(c.Title) || string.IsNullOrWhiteSpace(c.Body)) { TempData["err"] = Lang.T("Title and text are required."); return RedirectToPage(); }
        if (c.Id == 0) db.CannedResponses.Add(c); else db.CannedResponses.Update(c);
        await db.SaveChangesAsync();
        TempData["ok"] = Lang.T("Saved.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await db.CannedResponses.Where(c => c.Id == id).ExecuteDeleteAsync();
        TempData["ok"] = Lang.T("Deleted.");
        return RedirectToPage();
    }
}
