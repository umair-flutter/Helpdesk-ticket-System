using Help_DeskTicket_System.Data;
using Help_DeskTicket_System.Models;
using Help_DeskTicket_System.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Help_DeskTicket_System.Pages;

public class IndexModel(AppDbContext db) : PageModel
{
    public record Bucket(string Name, string Color, int Count);

    public int Open, Unassigned, Overdue, Mine, CreatedToday, ResolvedToday, WaitingOnMe;
    public double? AvgRating;
    public List<Bucket> ByStatus = [], ByPriority = [];
    public List<(DateTime Day, int Count)> Last14 = [];
    public List<Ticket> Recent = [];
    public List<(string Agent, int Open)> AgentLoad = [];
    public List<KbArticle> PopularKb = [];

    public async Task OnGetAsync()
    {
        var me = User.Id();
        var q = db.Tickets.VisibleTo(User);
        var today = DateTime.UtcNow.Date;
        var now = DateTime.UtcNow;

        Open = await q.CountAsync(t => !t.Status!.IsClosed);
        Overdue = await q.CountAsync(t => !t.Status!.IsClosed && t.ResolveDueAt < now);
        CreatedToday = await q.CountAsync(t => t.CreatedAt >= today);
        ResolvedToday = await q.CountAsync(t => t.ResolvedAt >= today);

        ByStatus = await q.GroupBy(t => new { t.Status!.Name, t.Status.Color, t.Status.SortOrder })
            .Select(g => new { g.Key, C = g.Count() }).OrderBy(x => x.Key.SortOrder)
            .Select(x => new Bucket(x.Key.Name, x.Key.Color, x.C)).ToListAsync();

        Recent = await q.Include(t => t.Status).Include(t => t.Priority).Include(t => t.AssignedTo)
            .OrderByDescending(t => t.UpdatedAt).Take(8).ToListAsync();

        if (User.IsStaff())
        {
            Unassigned = await q.CountAsync(t => t.AssignedToId == null && !t.Status!.IsClosed);
            Mine = await q.CountAsync(t => t.AssignedToId == me && !t.Status!.IsClosed);
            AvgRating = await q.Where(t => t.Rating != null).AverageAsync(t => (double?)t.Rating);

            ByPriority = await q.Where(t => !t.Status!.IsClosed)
                .GroupBy(t => new { t.Priority!.Name, t.Priority.Color, t.Priority.SortOrder })
                .Select(g => new { g.Key, C = g.Count() }).OrderByDescending(x => x.Key.SortOrder)
                .Select(x => new Bucket(x.Key.Name, x.Key.Color, x.C)).ToListAsync();

            var from = today.AddDays(-13);
            var created = await q.Where(t => t.CreatedAt >= from).Select(t => t.CreatedAt).ToListAsync();
            Last14 = Enumerable.Range(0, 14).Select(i => from.AddDays(i))
                .Select(d => (d, created.Count(c => c.Date == d))).ToList();

            AgentLoad = (await q.Where(t => t.AssignedToId != null && !t.Status!.IsClosed)
                    .GroupBy(t => new { t.AssignedTo!.FullName, t.AssignedTo.Email })
                    .Select(g => new { g.Key.FullName, g.Key.Email, C = g.Count() }).ToListAsync())
                .OrderByDescending(x => x.C).Take(8).Select(x => (string.IsNullOrEmpty(x.FullName) ? x.Email! : x.FullName, x.C)).ToList();
        }
        else
        {
            WaitingOnMe = await q.CountAsync(t => t.Status!.Name.Contains("Waiting"));
            PopularKb = await db.KbArticles.Where(k => k.IsPublished).OrderByDescending(k => k.Views).Take(5).ToListAsync();
        }
    }
}
