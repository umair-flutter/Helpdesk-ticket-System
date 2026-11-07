using System.Text.RegularExpressions;
using Help_DeskTicket_System.Data;
using Help_DeskTicket_System.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace Help_DeskTicket_System.Services;

/// <summary>
/// Email-to-ticket: polls the configured IMAP inbox. Unread mail whose subject contains [HD-00042]
/// becomes a reply on that ticket; anything else becomes a new ticket. Unknown senders get a client account.
/// </summary>
public partial class EmailIngestionService(IServiceScopeFactory scopes, ILogger<EmailIngestionService> log) : BackgroundService
{
    [GeneratedRegex(@"\[HD-(\d+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex TicketToken();

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var minutes = 5;
            try
            {
                using var scope = scopes.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
                minutes = Math.Max(1, await settings.GetIntAsync(SettingKeys.ImapPollMinutes, 5));
                if (await settings.GetBoolAsync(SettingKeys.ImapEnabled))
                    await PollAsync(scope.ServiceProvider, stop);
            }
            catch (Exception ex) when (!stop.IsCancellationRequested)
            {
                log.LogWarning(ex, "Email ingestion failed");
            }
            await Task.Delay(TimeSpan.FromMinutes(minutes), stop).ContinueWith(_ => { });
        }
    }

    public static async Task<int> PollAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        var s = await sp.GetRequiredService<SettingsService>().GetAllAsync();
        var tickets = sp.GetRequiredService<TicketService>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var db = sp.GetRequiredService<AppDbContext>();

        using var client = new ImapClient();
        var port = int.TryParse(s.GetValueOrDefault(SettingKeys.ImapPort), out var p) ? p : 993;
        var ssl = !bool.TryParse(s.GetValueOrDefault(SettingKeys.ImapUseSsl), out var x) || x;
        await client.ConnectAsync(s.GetValueOrDefault(SettingKeys.ImapHost) ?? "", port,
            ssl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.Auto, ct);
        await client.AuthenticateAsync(s.GetValueOrDefault(SettingKeys.ImapUser) ?? "", s.GetValueOrDefault(SettingKeys.ImapPassword) ?? "", ct);

        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite, ct);
        var ids = await inbox.SearchAsync(SearchQuery.NotSeen, ct);
        var processed = 0;

        foreach (var uid in ids.Take(50))
        {
            var msg = await inbox.GetMessageAsync(uid, ct);
            var from = msg.From.Mailboxes.FirstOrDefault();
            if (from is null) continue;

            var user = await users.FindByEmailAsync(from.Address);
            if (user is null)
            {
                user = new ApplicationUser { UserName = from.Address, Email = from.Address, FullName = from.Name ?? from.Address, EmailConfirmed = true };
                var created = await users.CreateAsync(user);
                if (!created.Succeeded) continue;
                await users.AddToRoleAsync(user, Roles.Client);
            }

            var body = (msg.TextBody ?? StripHtml(msg.HtmlBody) ?? "").Trim();
            var files = msg.Attachments.OfType<MimePart>().Select(a =>
            {
                var ms = new MemoryStream();
                a.Content?.DecodeTo(ms);
                ms.Position = 0;
                return new IncomingFile(a.FileName ?? "attachment", a.ContentType.MimeType, ms);
            }).ToList();

            var match = TicketToken().Match(msg.Subject ?? "");
            Ticket? existing = null;
            if (match.Success && int.TryParse(match.Groups[1].Value, out var tid))
                existing = await db.Tickets.FirstOrDefaultAsync(t => t.Id == tid, ct);

            if (existing is not null)
                await tickets.AddReplyAsync(existing.Id, user.Id, StripQuoted(body), false, files);
            else
                await tickets.CreateAsync(new Ticket
                {
                    Subject = string.IsNullOrWhiteSpace(msg.Subject) ? "(no subject)" : msg.Subject[..Math.Min(200, msg.Subject.Length)],
                    Description = string.IsNullOrWhiteSpace(body) ? "(empty email)" : body,
                    CreatedById = user.Id,
                    Source = TicketSource.Email
                }, user.Id, files);

            await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
            processed++;
        }
        await client.DisconnectAsync(true, ct);
        return processed;
    }

    private static string? StripHtml(string? html) =>
        html is null ? null : System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " "));

    /// <summary>Drop the quoted history below "On ... wrote:" so replies stay readable.</summary>
    private static string StripQuoted(string body)
    {
        var idx = Regex.Match(body, @"^\s*On .+wrote:\s*$", RegexOptions.Multiline);
        return idx.Success && idx.Index > 0 ? body[..idx.Index].Trim() : body;
    }
}

/// <summary>Every few minutes: alert on SLA breaches and auto-close tickets left resolved for N days.</summary>
public class SlaMonitorService(IServiceScopeFactory scopes, ILogger<SlaMonitorService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var notifier = scope.ServiceProvider.GetRequiredService<NotificationService>();
                var tickets = scope.ServiceProvider.GetRequiredService<TicketService>();
                var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
                var now = DateTime.UtcNow;

                var overdue = await db.Tickets.Where(t => !t.OverdueNotified && !t.Status!.IsClosed && t.ResolveDueAt < now).OrderBy(t => t.Id)
                    .Take(100).ToListAsync(stop);
                if (overdue.Count > 0)
                {
                    var admins = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                        .GetUsersInRoleAsync(Roles.Admin)).Select(a => a.Id).ToList();
                    foreach (var t in overdue)
                    {
                        t.OverdueNotified = true;
                        tickets.Log(t.Id, null, "SLA breached — resolution overdue");
                        await notifier.NotifyAsync([.. admins, t.AssignedToId], $"SLA breached: {t.Number} {t.Subject}", $"/Tickets/Details/{t.Id}");
                    }
                    await db.SaveChangesAsync(stop);
                }

                var days = await settings.GetIntAsync(SettingKeys.AutoCloseDays, 0);
                if (days > 0)
                {
                    var closed = await db.Statuses.Where(s => s.IsClosed).OrderByDescending(s => s.SortOrder).FirstOrDefaultAsync(stop);
                    var cutoff = now.AddDays(-days);
                    if (closed is not null)
                    {
                        var stale = await db.Tickets.Include(t => t.Status)
                            .Where(t => t.Status!.IsClosed && t.StatusId != closed.Id && t.ResolvedAt < cutoff).OrderBy(t => t.Id).Take(100).ToListAsync(stop);
                        foreach (var t in stale) await tickets.ChangeStatusAsync(t, closed.Id, null);
                        if (stale.Count > 0) await db.SaveChangesAsync(stop);
                    }
                }
            }
            catch (Exception ex) when (!stop.IsCancellationRequested)
            {
                log.LogWarning(ex, "SLA monitor failed");
            }
            await Task.Delay(TimeSpan.FromMinutes(2), stop).ContinueWith(_ => { });
        }
    }
}
