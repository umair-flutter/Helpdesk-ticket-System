using Help_DeskTicket_System.Data;
using Help_DeskTicket_System.Models;
using Help_DeskTicket_System.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Help_DeskTicket_System.Pages.Tickets;

public class DetailsModel(AppDbContext db, TicketService tickets, UserManager<ApplicationUser> users, SettingsService settings) : PageModel
{
    public Ticket Ticket { get; set; } = null!;
    public List<TicketReply> Replies { get; set; } = [];
    public List<TicketActivity> Activities { get; set; } = [];
    public HashSet<string> StaffIds { get; set; } = [];
    public List<TicketStatus> Statuses { get; set; } = [];
    public List<Priority> Priorities { get; set; } = [];
    public List<Department> Departments { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
    public List<ApplicationUser> Agents { get; set; } = [];
    public List<CannedResponse> Canned { get; set; } = [];
    public List<Ticket> OtherTickets { get; set; } = [];
    public bool CanReopen { get; set; }

    private async Task<Ticket?> FindAsync(int id) =>
        await db.Tickets.VisibleTo(User)
            .Include(t => t.Status).Include(t => t.Priority).Include(t => t.Department).Include(t => t.Category)
            .Include(t => t.CreatedBy).Include(t => t.AssignedTo).Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == id);

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var t = await FindAsync(id);
        if (t is null) return NotFound();
        Ticket = t;
        var staff = User.IsStaff();

        if (staff && !t.SeenByStaff) { t.SeenByStaff = true; await db.SaveChangesAsync(); }

        Replies = await db.Replies.Include(r => r.Author).Where(r => r.TicketId == id && (staff || !r.IsInternal))
            .OrderBy(r => r.CreatedAt).ToListAsync();
        StaffIds = (await tickets.StaffIdsAsync()).ToHashSet();
        Statuses = await db.Statuses.OrderBy(s => s.SortOrder).ToListAsync();
        CanReopen = await settings.GetBoolAsync(SettingKeys.ClientsCanReopen, true);

        if (staff)
        {
            Activities = await db.Activities.Include(a => a.User).Where(a => a.TicketId == id).OrderByDescending(a => a.CreatedAt).ToListAsync();
            Priorities = await db.Priorities.OrderBy(p => p.SortOrder).ToListAsync();
            Departments = await db.Departments.OrderBy(d => d.Name).ToListAsync();
            Categories = await db.Categories.OrderBy(c => c.Name).ToListAsync();
            Canned = await db.CannedResponses.OrderBy(c => c.Title).ToListAsync();
            Agents = (await users.GetUsersInRoleAsync(Roles.Agent)).Concat(await users.GetUsersInRoleAsync(Roles.Admin))
                .Where(u => u.IsActive).DistinctBy(u => u.Id).OrderBy(u => u.DisplayName).ToList();
            OtherTickets = await db.Tickets.Include(x => x.Status)
                .Where(x => x.CreatedById == t.CreatedById && x.Id != t.Id).OrderByDescending(x => x.CreatedAt).Take(5).ToListAsync();
        }
        return Page();
    }

    public async Task<IActionResult> OnPostReplyAsync(int id, string body, bool isInternal, int? setStatusId, List<IFormFile> files)
    {
        var t = await FindAsync(id);
        if (t is null) return NotFound();
        if (string.IsNullOrWhiteSpace(body)) { TempData["err"] = Lang.T("Reply can't be empty."); return RedirectToPage(new { id }); }

        await tickets.AddReplyAsync(id, User.Id(), body.Trim(), isInternal && User.IsStaff(), TicketService.FromForm(files));

        if (User.IsStaff() && setStatusId.HasValue && setStatusId != t.StatusId)
        {
            var fresh = await db.Tickets.Include(x => x.Status).FirstAsync(x => x.Id == id);
            await tickets.ChangeStatusAsync(fresh, setStatusId.Value, User.Id());
            await db.SaveChangesAsync();
        }
        TempData["ok"] = Lang.T(isInternal ? "Internal note added." : "Reply sent.");
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, int statusId, int priorityId, string? assignedToId, int? departmentId, int? categoryId, string? tags)
    {
        if (!User.IsStaff()) return Forbid();
        if (await FindAsync(id) is null) return NotFound();
        await tickets.UpdateAsync(id, User.Id(), statusId, priorityId, assignedToId, departmentId, categoryId, tags);
        TempData["ok"] = Lang.T("Ticket updated.");
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostTakeAsync(int id)
    {
        if (!User.IsStaff()) return Forbid();
        var t = await FindAsync(id);
        if (t is null) return NotFound();
        await tickets.UpdateAsync(id, User.Id(), t.StatusId, t.PriorityId, User.Id(), t.DepartmentId, t.CategoryId, t.Tags);
        TempData["ok"] = Lang.T("You took this ticket.");
        return RedirectToPage(new { id });
    }

    /// <summary>Requester closes their own ticket, or reopens it.</summary>
    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var t = await FindAsync(id);
        if (t is null) return NotFound();
        if (t.Status!.IsClosed)
        {
            if (!User.IsStaff() && !await settings.GetBoolAsync(SettingKeys.ClientsCanReopen, true)) return Forbid();
            var open = await db.Statuses.OrderByDescending(s => s.IsDefault).ThenBy(s => s.SortOrder).FirstAsync(s => !s.IsClosed);
            await tickets.ChangeStatusAsync(t, open.Id, User.Id());
        }
        else
        {
            var closed = await db.Statuses.Where(s => s.IsClosed).OrderBy(s => s.SortOrder).FirstAsync();
            await tickets.ChangeStatusAsync(t, closed.Id, User.Id());
        }
        await db.SaveChangesAsync();
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRateAsync(int id, int rating, string? comment)
    {
        var t = await FindAsync(id);
        if (t is null || t.CreatedById != User.Id() || !t.Status!.IsClosed || rating is < 1 or > 5) return BadRequest();
        t.Rating = rating;
        t.RatingComment = comment?.Trim();
        tickets.Log(t.Id, User.Id(), $"Rated the support {rating}/5");
        await db.SaveChangesAsync();
        TempData["ok"] = Lang.T("Thanks for your feedback!");
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (!User.IsInRole(Roles.Admin)) return Forbid();
        var t = await db.Tickets.Include(x => x.Attachments).FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();
        foreach (var a in t.Attachments)
        {
            var p = Path.Combine(tickets.UploadRoot, a.StoredName);
            if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
        }
        db.Tickets.Remove(t);
        await db.SaveChangesAsync();
        TempData["ok"] = Lang.T("{0} deleted.", t.Number);
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnGetFileAsync(int id, int fileId)
    {
        var t = await FindAsync(id);
        if (t is null) return NotFound();
        var a = t.Attachments.FirstOrDefault(x => x.Id == fileId);
        if (a is null) return NotFound();
        if (a.ReplyId.HasValue && !User.IsStaff() && await db.Replies.AnyAsync(r => r.Id == a.ReplyId && r.IsInternal)) return NotFound();

        var path = Path.Combine(tickets.UploadRoot, a.StoredName);
        if (!System.IO.File.Exists(path)) return NotFound();
        var inline = a.ContentType.StartsWith("image/") || a.ContentType == "application/pdf";
        return inline ? PhysicalFile(path, a.ContentType) : PhysicalFile(path, a.ContentType, a.FileName);
    }
}
