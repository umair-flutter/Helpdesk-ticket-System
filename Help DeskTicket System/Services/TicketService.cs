using System.Net;
using Help_DeskTicket_System.Data;
using Help_DeskTicket_System.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Help_DeskTicket_System.Services;

public record IncomingFile(string FileName, string ContentType, Stream Content);

/// <summary>All ticket workflow lives here so web pages and the email poller behave the same way.</summary>
public class TicketService(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    NotificationService notifier,
    EmailService email,
    SettingsService settings,
    IWebHostEnvironment env)
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    public static readonly string[] AllowedExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".pdf", ".txt", ".log", ".csv", ".doc", ".docx", ".xls", ".xlsx", ".zip"];

    public string UploadRoot => Path.Combine(env.ContentRootPath, "App_Data", "uploads");

    public async Task<List<string>> StaffIdsAsync()
    {
        var admins = await users.GetUsersInRoleAsync(Roles.Admin);
        var agents = await users.GetUsersInRoleAsync(Roles.Agent);
        return admins.Concat(agents).Where(u => u.IsActive).Select(u => u.Id).Distinct().ToList();
    }

    public async Task<bool> IsStaffAsync(string? userId)
    {
        if (userId is null) return false;
        var u = await users.FindByIdAsync(userId);
        return u is not null && (await users.IsInRoleAsync(u, Roles.Admin) || await users.IsInRoleAsync(u, Roles.Agent));
    }

    // ---------------------------------------------------------------- create

    public async Task<Ticket> CreateAsync(Ticket t, string? actorId, IEnumerable<IncomingFile>? files = null)
    {
        t.StatusId = t.StatusId != 0 ? t.StatusId
            : (await db.Statuses.OrderByDescending(s => s.IsDefault).ThenBy(s => s.SortOrder).FirstAsync()).Id;
        if (t.PriorityId == 0)
            t.PriorityId = (await db.Priorities.OrderByDescending(p => p.IsDefault).ThenBy(p => p.SortOrder).FirstAsync()).Id;

        var priority = await db.Priorities.FindAsync(t.PriorityId) ?? throw new InvalidOperationException("Unknown priority");
        t.CreatedAt = t.UpdatedAt = DateTime.UtcNow;
        t.ResponseDueAt = t.CreatedAt.AddHours(priority.SlaResponseHours);
        t.ResolveDueAt = t.CreatedAt.AddHours(priority.SlaResolveHours);

        db.Tickets.Add(t);
        await db.SaveChangesAsync();

        if (files is not null) await SaveFilesAsync(t.Id, null, files);
        Log(t.Id, actorId, $"Ticket created via {t.Source}");

        if (t.AssignedToId is null && await settings.GetBoolAsync(SettingKeys.AutoAssign, true))
            await AutoAssignAsync(t);

        await db.SaveChangesAsync();

        // Notify: assigned agent if any, else all staff.
        var link = $"/Tickets/Details/{t.Id}";
        var recipients = t.AssignedToId is not null ? [t.AssignedToId] : await StaffIdsAsync();
        await notifier.NotifyAsync(recipients, $"New ticket {t.Number}: {t.Subject}", link, actorId);

        var creator = t.CreatedById is null ? null : await users.FindByIdAsync(t.CreatedById);
        await email.SendAsync(creator?.Email, $"[{t.Number}] {t.Subject}",
            $"<p>Hi {Enc(creator?.DisplayName)},</p><p>We received your request and created ticket <b>{t.Number}</b>. " +
            "Reply to this email to add more details.</p>");
        return t;
    }

    /// <summary>Assign to the active agent with the fewest open tickets, preferring the ticket's department.</summary>
    public async Task AutoAssignAsync(Ticket t)
    {
        var agents = (await users.GetUsersInRoleAsync(Roles.Agent)).Where(a => a.IsActive).ToList();
        if (agents.Count == 0) return;

        var pool = agents.Where(a => t.DepartmentId.HasValue && a.DepartmentId == t.DepartmentId).ToList();
        if (pool.Count == 0) pool = agents;

        var ids = pool.Select(a => a.Id).ToList();
        var load = await db.Tickets.Where(x => x.AssignedToId != null && ids.Contains(x.AssignedToId) && !x.Status!.IsClosed)
            .GroupBy(x => x.AssignedToId!).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var pick = pool.OrderBy(a => load.GetValueOrDefault(a.Id)).ThenBy(a => a.CreatedAt).First();
        t.AssignedToId = pick.Id;
        Log(t.Id, null, $"Auto-assigned to {pick.DisplayName}");
    }

    // ---------------------------------------------------------------- reply

    public async Task<TicketReply> AddReplyAsync(int ticketId, string? authorId, string body, bool isInternal,
        IEnumerable<IncomingFile>? files = null)
    {
        var t = await db.Tickets.Include(x => x.Status).Include(x => x.CreatedBy).FirstAsync(x => x.Id == ticketId);
        var isStaff = await IsStaffAsync(authorId);
        if (!isStaff) isInternal = false;

        var reply = new TicketReply { TicketId = ticketId, AuthorId = authorId, Body = body, IsInternal = isInternal };
        db.Replies.Add(reply);
        t.UpdatedAt = DateTime.UtcNow;

        if (isStaff && !isInternal && t.FirstResponseAt is null && authorId != t.CreatedById)
            t.FirstResponseAt = DateTime.UtcNow;

        // A client replying to a closed ticket reopens it (if allowed).
        if (!isStaff && t.Status!.IsClosed && await settings.GetBoolAsync(SettingKeys.ClientsCanReopen, true))
        {
            var open = await db.Statuses.OrderByDescending(s => s.IsDefault).ThenBy(s => s.SortOrder).FirstAsync(s => !s.IsClosed);
            t.StatusId = open.Id;
            t.ResolvedAt = null;
            Log(t.Id, authorId, $"Reopened by customer reply (status → {open.Name})");
        }

        await db.SaveChangesAsync();
        if (files is not null) await SaveFilesAsync(ticketId, reply.Id, files);
        Log(t.Id, authorId, isInternal ? "Added an internal note" : "Replied");
        await db.SaveChangesAsync();

        var link = $"/Tickets/Details/{t.Id}";
        if (isStaff)
        {
            var staffTargets = new List<string?> { t.AssignedToId };
            if (!isInternal) staffTargets.Add(t.CreatedById);
            await notifier.NotifyAsync(staffTargets, $"{(isInternal ? "Internal note" : "New reply")} on {t.Number}", link, authorId);
            if (!isInternal && t.CreatedById != authorId)
                await email.SendAsync(t.CreatedBy?.Email, $"Re: [{t.Number}] {t.Subject}",
                    $"<div style=\"white-space:pre-wrap\">{Enc(body)}</div><hr><small>Reply to this email or visit the help desk to respond.</small>");
        }
        else
        {
            var targets = t.AssignedToId is not null ? [t.AssignedToId] : await StaffIdsAsync();
            await notifier.NotifyAsync(targets, $"Customer replied on {t.Number}", link, authorId);
        }
        return reply;
    }

    // ---------------------------------------------------------------- update fields

    public async Task UpdateAsync(int ticketId, string? actorId, int statusId, int priorityId, string? assignedToId,
        int? departmentId, int? categoryId, string? tags)
    {
        var t = await db.Tickets.Include(x => x.Status).Include(x => x.Priority).Include(x => x.AssignedTo)
            .FirstAsync(x => x.Id == ticketId);
        var link = $"/Tickets/Details/{t.Id}";
        assignedToId = string.IsNullOrEmpty(assignedToId) ? null : assignedToId;

        if (t.StatusId != statusId) await ChangeStatusAsync(t, statusId, actorId);

        if (t.PriorityId != priorityId)
        {
            var p = await db.Priorities.FindAsync(priorityId) ?? throw new InvalidOperationException("Unknown priority");
            Log(t.Id, actorId, $"Priority {t.Priority?.Name} → {p.Name}");
            t.PriorityId = p.Id;
            // Recalculate SLA targets from creation time using the new priority.
            t.ResponseDueAt = t.CreatedAt.AddHours(p.SlaResponseHours);
            t.ResolveDueAt = t.CreatedAt.AddHours(p.SlaResolveHours);
            t.OverdueNotified = false;
        }

        if (t.AssignedToId != assignedToId)
        {
            var agent = assignedToId is null ? null : await users.FindByIdAsync(assignedToId);
            Log(t.Id, actorId, agent is null ? "Unassigned" : $"Assigned to {agent.DisplayName}");
            t.AssignedToId = assignedToId;
            if (assignedToId is not null)
                await notifier.NotifyAsync([assignedToId], $"{t.Number} was assigned to you", link, actorId);
        }

        if (t.DepartmentId != departmentId) { Log(t.Id, actorId, "Department changed"); t.DepartmentId = departmentId; }
        if (t.CategoryId != categoryId) { Log(t.Id, actorId, "Category changed"); t.CategoryId = categoryId; }
        t.Tags = string.IsNullOrWhiteSpace(tags) ? null : tags.Trim();
        t.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task ChangeStatusAsync(Ticket t, int statusId, string? actorId)
    {
        var s = await db.Statuses.FindAsync(statusId) ?? throw new InvalidOperationException("Unknown status");
        var old = t.Status?.Name ?? (await db.Statuses.FindAsync(t.StatusId))?.Name;
        t.StatusId = s.Id;
        t.Status = s;
        t.ResolvedAt = s.IsClosed ? DateTime.UtcNow : null;
        t.UpdatedAt = DateTime.UtcNow;
        Log(t.Id, actorId, $"Status {old} → {s.Name}");

        await notifier.NotifyAsync([t.CreatedById, t.AssignedToId], $"{t.Number} is now {s.Name}", $"/Tickets/Details/{t.Id}", actorId);
        if (s.IsClosed && t.CreatedById != actorId)
        {
            var creator = t.CreatedById is null ? null : await users.FindByIdAsync(t.CreatedById);
            await email.SendAsync(creator?.Email, $"[{t.Number}] {t.Subject} — {s.Name}",
                $"<p>Your ticket <b>{t.Number}</b> was marked <b>{Enc(s.Name)}</b>. " +
                "You can rate the support you received on the ticket page.</p>");
        }
    }

    // ---------------------------------------------------------------- files

    public async Task SaveFilesAsync(int ticketId, int? replyId, IEnumerable<IncomingFile> files)
    {
        Directory.CreateDirectory(UploadRoot);
        foreach (var f in files)
        {
            var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext)) continue;

            var stored = $"{Guid.NewGuid():N}{ext}";
            var path = Path.Combine(UploadRoot, stored);
            long size;
            await using (var fs = File.Create(path))
            {
                await f.Content.CopyToAsync(fs);
                size = fs.Length;
            }
            if (size > MaxFileBytes) { File.Delete(path); continue; }

            db.Attachments.Add(new Attachment
            {
                TicketId = ticketId, ReplyId = replyId, FileName = Path.GetFileName(f.FileName),
                StoredName = stored, ContentType = f.ContentType, Size = size
            });
        }
        await db.SaveChangesAsync();
    }

    public static IEnumerable<IncomingFile> FromForm(IEnumerable<IFormFile>? files) =>
        (files ?? []).Where(f => f.Length > 0 && f.Length <= MaxFileBytes)
            .Select(f => new IncomingFile(f.FileName, f.ContentType, f.OpenReadStream()));

    public void Log(int ticketId, string? userId, string action) =>
        db.Activities.Add(new TicketActivity { TicketId = ticketId, UserId = userId, Action = action });

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
}
