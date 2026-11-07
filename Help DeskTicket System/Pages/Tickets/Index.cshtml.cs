using System.Text;
using Help_DeskTicket_System.Data;
using Help_DeskTicket_System.Models;
using Help_DeskTicket_System.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Help_DeskTicket_System.Pages.Tickets;

public class IndexModel(AppDbContext db, UserManager<ApplicationUser> users, TicketService tickets) : PageModel
{
    public const int PageSize = 25;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? View { get; set; }        // open | closed | mine | unassigned | overdue
    [BindProperty(SupportsGet = true)] public int? StatusId { get; set; }
    [BindProperty(SupportsGet = true)] public int? PriorityId { get; set; }
    [BindProperty(SupportsGet = true)] public int? DepartmentId { get; set; }
    [BindProperty(SupportsGet = true)] public string? AssigneeId { get; set; }
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "updated";
    [BindProperty(SupportsGet = true)] public int P { get; set; } = 1;

    public List<Ticket> Items { get; set; } = [];
    public int Total { get; set; }
    public List<TicketStatus> Statuses { get; set; } = [];
    public List<Priority> Priorities { get; set; } = [];
    public List<Department> Departments { get; set; } = [];
    public List<ApplicationUser> Agents { get; set; } = [];

    private IQueryable<Ticket> Filtered()
    {
        var me = User.Id();
        var now = DateTime.UtcNow;
        var q = db.Tickets.VisibleTo(User);

        q = View switch
        {
            "open" => q.Where(t => !t.Status!.IsClosed),
            "closed" => q.Where(t => t.Status!.IsClosed),
            "mine" => q.Where(t => t.AssignedToId == me && !t.Status!.IsClosed),
            "unassigned" => q.Where(t => t.AssignedToId == null && !t.Status!.IsClosed),
            "overdue" => q.Where(t => !t.Status!.IsClosed && t.ResolveDueAt < now),
            _ => q
        };
        if (StatusId.HasValue) q = q.Where(t => t.StatusId == StatusId);
        if (PriorityId.HasValue) q = q.Where(t => t.PriorityId == PriorityId);
        if (DepartmentId.HasValue) q = q.Where(t => t.DepartmentId == DepartmentId);
        if (!string.IsNullOrEmpty(AssigneeId)) q = q.Where(t => t.AssignedToId == AssigneeId);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            var idPart = term.StartsWith("HD-", StringComparison.OrdinalIgnoreCase) ? term[3..] : term;
            var hasId = int.TryParse(idPart, out var id);
            q = q.Where(t => (hasId && t.Id == id) || t.Subject.Contains(term) || t.Description.Contains(term)
                             || (t.Tags != null && t.Tags.Contains(term)) || t.CreatedBy!.Email!.Contains(term));
        }

        return Sort switch
        {
            "created" => q.OrderByDescending(t => t.CreatedAt),
            "priority" => q.OrderByDescending(t => t.Priority!.SortOrder).ThenByDescending(t => t.UpdatedAt),
            "due" => q.OrderBy(t => t.Status!.IsClosed).ThenBy(t => t.ResolveDueAt),
            _ => q.OrderByDescending(t => t.UpdatedAt)
        };
    }

    public async Task OnGetAsync()
    {
        await LoadListsAsync();
        var q = Filtered();
        Total = await q.CountAsync();
        P = Math.Max(1, P);
        Items = await q.Include(t => t.Status).Include(t => t.Priority).Include(t => t.AssignedTo)
            .Include(t => t.CreatedBy).Include(t => t.Department)
            .Skip((P - 1) * PageSize).Take(PageSize).ToListAsync();
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        var rows = await Filtered().Include(t => t.Status).Include(t => t.Priority).Include(t => t.AssignedTo)
            .Include(t => t.CreatedBy).Include(t => t.Department).Include(t => t.Category).Take(50_000).ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Number,Subject,Status,Priority,Department,Category,Requester,Assignee,Source,Tags,Created (UTC),Updated (UTC),First response (UTC),Resolved (UTC),Resolve due (UTC),Overdue,Rating");
        foreach (var t in rows)
            sb.AppendLine(string.Join(',', new[]
            {
                t.Number, t.Subject, t.Status?.Name, t.Priority?.Name, t.Department?.Name, t.Category?.Name,
                t.CreatedBy?.Email, t.AssignedTo?.Email, t.Source.ToString(), t.Tags,
                t.CreatedAt.ToString("u"), t.UpdatedAt.ToString("u"), t.FirstResponseAt?.ToString("u"),
                t.ResolvedAt?.ToString("u"), t.ResolveDueAt?.ToString("u"), t.IsOverdue ? "yes" : "no", t.Rating?.ToString()
            }.Select(Csv)));
        // BOM so Excel opens UTF-8 correctly
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(),
            "text/csv", $"tickets-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    public static string Csv(string? v)
    {
        v ??= "";
        if (v.Length > 0 && "=+-@".Contains(v[0])) v = "'" + v; // block CSV formula injection
        return v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }

    /// <summary>Bulk actions for staff: assign, change status, or delete (admin only).</summary>
    public async Task<IActionResult> OnPostBulkAsync(int[] ids, string action, int? statusId, string? assigneeId)
    {
        if (!User.IsStaff()) return Forbid();
        if (ids.Length == 0) { TempData["err"] = Lang.T("Select at least one ticket."); return RedirectToPage(); }

        var list = await db.Tickets.Include(t => t.Status).Include(t => t.Priority).Include(t => t.AssignedTo)
            .Where(t => ids.Contains(t.Id)).ToListAsync();
        switch (action)
        {
            case "status" when statusId.HasValue:
                foreach (var t in list) await tickets.ChangeStatusAsync(t, statusId.Value, User.Id());
                break;
            case "assign":
                foreach (var t in list)
                    await tickets.UpdateAsync(t.Id, User.Id(), t.StatusId, t.PriorityId, assigneeId, t.DepartmentId, t.CategoryId, t.Tags);
                break;
            case "delete" when User.IsInRole(Roles.Admin):
                db.Tickets.RemoveRange(list);
                break;
            default:
                TempData["err"] = Lang.T("Choose a valid bulk action.");
                return RedirectToPage();
        }
        await db.SaveChangesAsync();
        TempData["ok"] = Lang.T("Updated {0} ticket(s).", list.Count);
        return RedirectToPage();
    }

    private async Task LoadListsAsync()
    {
        Statuses = await db.Statuses.OrderBy(s => s.SortOrder).ToListAsync();
        Priorities = await db.Priorities.OrderBy(p => p.SortOrder).ToListAsync();
        Departments = await db.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync();
        if (User.IsStaff())
            Agents = (await users.GetUsersInRoleAsync(Roles.Agent)).Concat(await users.GetUsersInRoleAsync(Roles.Admin))
                .Where(u => u.IsActive).DistinctBy(u => u.Id).OrderBy(u => u.DisplayName).ToList();
    }

    public string PageUrl(int p) => Url.Page("Index", new { Q, View, StatusId, PriorityId, DepartmentId, AssigneeId, Sort, P = p })!;
}
