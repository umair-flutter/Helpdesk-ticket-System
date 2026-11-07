using System.Text;
using Help_DeskTicket_System.Data;
using Help_DeskTicket_System.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Help_DeskTicket_System.Pages;

public class ReportsModel(AppDbContext db) : PageModel
{
    public record AgentRow(string Agent, int Assigned, int Resolved, TimeSpan? AvgFirstResponse, TimeSpan? AvgResolution, double? SlaMet, double? Rating);
    public record Group(string Name, int Count, int Resolved);

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }

    public int Created, Resolved, OpenNow, Rated;
    public TimeSpan? AvgFirstResponse, AvgResolution;
    public double? ResponseSla, ResolveSla, AvgRating;
    public List<AgentRow> Agents = [];
    public List<Group> ByDepartment = [], ByCategory = [], ByPriority = [], BySource = [];
    public List<(DateTime Day, int Created, int Resolved)> Daily = [];

    private List<Ticket> _rows = [];

    private async Task LoadAsync()
    {
        var to = (To ?? DateTime.Today).Date;
        var from = (From ?? to.AddDays(-29)).Date;
        if (from > to) (from, to) = (to, from);
        From = from; To = to;
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.AddDays(1).ToUniversalTime();

        _rows = await db.Tickets.AsNoTracking()
            .Include(t => t.Status).Include(t => t.Priority).Include(t => t.Department).Include(t => t.Category).Include(t => t.AssignedTo)
            .Where(t => t.CreatedAt >= fromUtc && t.CreatedAt < toUtc).ToListAsync();

        Created = _rows.Count;
        var resolved = _rows.Where(t => t.Status!.IsClosed && t.ResolvedAt.HasValue).ToList();
        Resolved = resolved.Count;
        OpenNow = await db.Tickets.CountAsync(t => !t.Status!.IsClosed);

        AvgFirstResponse = Avg(_rows.Where(t => t.FirstResponseAt.HasValue).Select(t => t.FirstResponseAt!.Value - t.CreatedAt));
        AvgResolution = Avg(resolved.Select(t => t.ResolvedAt!.Value - t.CreatedAt));
        ResponseSla = Pct(_rows.Where(t => t.FirstResponseAt.HasValue), t => t.FirstResponseAt <= t.ResponseDueAt);
        ResolveSla = Pct(resolved, t => t.ResolvedAt <= t.ResolveDueAt);
        var rated = _rows.Where(t => t.Rating.HasValue).ToList();
        Rated = rated.Count;
        AvgRating = rated.Count == 0 ? null : rated.Average(t => t.Rating!.Value);

        Agents = _rows.Where(t => t.AssignedTo is not null).GroupBy(t => t.AssignedTo!.DisplayName).Select(g =>
        {
            var res = g.Where(t => t.Status!.IsClosed && t.ResolvedAt.HasValue).ToList();
            var r = g.Where(t => t.Rating.HasValue).ToList();
            return new AgentRow(g.Key, g.Count(), res.Count,
                Avg(g.Where(t => t.FirstResponseAt.HasValue).Select(t => t.FirstResponseAt!.Value - t.CreatedAt)),
                Avg(res.Select(t => t.ResolvedAt!.Value - t.CreatedAt)),
                Pct(res, t => t.ResolvedAt <= t.ResolveDueAt),
                r.Count == 0 ? null : r.Average(t => t.Rating!.Value));
        }).OrderByDescending(a => a.Resolved).ToList();

        ByDepartment = GroupBy(t => t.Department?.Name ?? "(none)");
        ByCategory = GroupBy(t => t.Category?.Name ?? "(none)");
        ByPriority = GroupBy(t => t.Priority?.Name ?? "(none)");
        BySource = GroupBy(t => t.Source.ToString());

        var resolvedInRange = await db.Tickets.AsNoTracking().Where(t => t.ResolvedAt >= fromUtc && t.ResolvedAt < toUtc)
            .Select(t => t.ResolvedAt!.Value).ToListAsync();
        Daily = Enumerable.Range(0, (to - from).Days + 1).Select(i => from.AddDays(i)).Select(d =>
            (d, _rows.Count(t => t.CreatedAt.ToLocalTime().Date == d), resolvedInRange.Count(r => r.ToLocalTime().Date == d))).ToList();
    }

    private List<Group> GroupBy(Func<Ticket, string> key) =>
        _rows.GroupBy(key).Select(g => new Group(g.Key, g.Count(), g.Count(t => t.Status!.IsClosed)))
            .OrderByDescending(g => g.Count).ToList();

    private static TimeSpan? Avg(IEnumerable<TimeSpan> spans)
    {
        var l = spans.ToList();
        return l.Count == 0 ? null : TimeSpan.FromTicks((long)l.Average(s => s.Ticks));
    }

    private static double? Pct<T>(IEnumerable<T> items, Func<T, bool> ok)
    {
        var l = items.ToList();
        return l.Count == 0 ? null : 100.0 * l.Count(ok) / l.Count;
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnGetExportAsync()
    {
        await LoadAsync();
        var sb = new StringBuilder();
        sb.AppendLine($"Report,{From:yyyy-MM-dd} to {To:yyyy-MM-dd}");
        sb.AppendLine($"Created,{Created}\nResolved,{Resolved}\nAvg first response (h),{AvgFirstResponse?.TotalHours:0.0}\nAvg resolution (h),{AvgResolution?.TotalHours:0.0}");
        sb.AppendLine($"First-response SLA met %,{ResponseSla:0.0}\nResolution SLA met %,{ResolveSla:0.0}\nAvg rating,{AvgRating:0.00}");
        sb.AppendLine();
        sb.AppendLine("Agent,Assigned,Resolved,Avg first response (h),Avg resolution (h),SLA met %,Avg rating");
        foreach (var a in Agents)
            sb.AppendLine($"{Tickets.IndexModel.Csv(a.Agent)},{a.Assigned},{a.Resolved},{a.AvgFirstResponse?.TotalHours:0.0},{a.AvgResolution?.TotalHours:0.0},{a.SlaMet:0.0},{a.Rating:0.00}");
        sb.AppendLine();
        sb.AppendLine("Department,Tickets,Resolved");
        foreach (var g in ByDepartment) sb.AppendLine($"{Tickets.IndexModel.Csv(g.Name)},{g.Count},{g.Resolved}");
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", $"report-{From:yyyyMMdd}-{To:yyyyMMdd}.csv");
    }
}
