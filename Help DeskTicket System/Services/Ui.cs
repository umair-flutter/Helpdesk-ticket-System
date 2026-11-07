using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Help_DeskTicket_System.Models;
using Microsoft.AspNetCore.Html;

namespace Help_DeskTicket_System.Services;

public static partial class Ui
{
    public static bool IsStaff(this ClaimsPrincipal u) => u.IsInRole(Roles.Admin) || u.IsInRole(Roles.Agent);
    public static string? Id(this ClaimsPrincipal u) => u.FindFirstValue(ClaimTypes.NameIdentifier);

    public static IHtmlContent Badge(string? text, string? color) =>
        new HtmlString($"<span class=\"badge\" style=\"background:{WebUtility.HtmlEncode(color ?? "#6c757d")}\">{WebUtility.HtmlEncode(text ?? "—")}</span>");

    public static IHtmlContent Status(TicketStatus? s) => Badge(s?.Name, s?.Color);
    public static IHtmlContent Priority(Priority? p) => Badge(p?.Name, p?.Color);

    public static string Ago(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        var future = d < TimeSpan.Zero;
        d = d.Duration();
        if (d.TotalMinutes < 1) return Lang.T("just now");
        if (d.TotalDays >= 30) return utc.ToLocalTime().ToString("d MMM yyyy");
        var (n, unit) = d.TotalHours < 1 ? ((int)d.TotalMinutes, "m") : d.TotalDays < 1 ? ((int)d.TotalHours, "h") : ((int)d.TotalDays, "d");
        return future ? Lang.T($"in {{0}}{unit}", n) : Lang.T($"{{0}}{unit} ago", n);
    }

    public static string Duration(TimeSpan? t) => t is null ? "—"
        : t.Value.TotalHours < 1 ? Lang.T("{0}m", t.Value.TotalMinutes.ToString("0"))
        : t.Value.TotalDays < 2 ? Lang.T("{0}h", t.Value.TotalHours.ToString("0.#"))
        : Lang.T("{0}d", t.Value.TotalDays.ToString("0.#"));

    public static string FileSize(long b) => b < 1024 ? $"{b} B" : b < 1048576 ? $"{b / 1024.0:0.#} KB" : $"{b / 1048576.0:0.#} MB";

    [GeneratedRegex(@"\*\*(.+?)\*\*")] private static partial Regex Bold();
    [GeneratedRegex(@"`([^`]+)`")] private static partial Regex Code();
    [GeneratedRegex(@"(https?://[^\s<]+)")] private static partial Regex Link();

    /// <summary>Safe, minimal markdown: HTML-encodes first, then **bold**, `code`, links, "- " lists and line breaks.</summary>
    public static IHtmlContent Text(string? raw)
    {
        var sb = new StringBuilder();
        var inList = false;
        foreach (var line in (raw ?? "").Replace("\r", "").Split('\n'))
        {
            var enc = WebUtility.HtmlEncode(line);
            enc = Bold().Replace(enc, "<strong>$1</strong>");
            enc = Code().Replace(enc, "<code>$1</code>");
            enc = Link().Replace(enc, "<a href=\"$1\" target=\"_blank\" rel=\"noopener noreferrer\">$1</a>");
            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                if (!inList) { sb.Append("<ul class=\"mb-2\">"); inList = true; }
                sb.Append("<li>").Append(enc[2..]).Append("</li>");
                continue;
            }
            if (inList) { sb.Append("</ul>"); inList = false; }
            sb.Append(enc).Append("<br>");
        }
        if (inList) sb.Append("</ul>");
        return new HtmlString(sb.ToString());
    }
}
