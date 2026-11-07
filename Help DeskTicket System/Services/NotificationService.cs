using Help_DeskTicket_System.Data;
using Help_DeskTicket_System.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Help_DeskTicket_System.Services;

/// <summary>SignalR hub; each authenticated user is addressed by their user id (Clients.User).</summary>
[Authorize]
public class NotificationHub : Hub;

public class NotificationService(AppDbContext db, IHubContext<NotificationHub> hub)
{
    public async Task NotifyAsync(IEnumerable<string?> userIds, string message, string? link, string? exceptUserId = null)
    {
        var targets = userIds.Where(id => !string.IsNullOrEmpty(id) && id != exceptUserId).Distinct().Cast<string>().ToList();
        if (targets.Count == 0) return;

        foreach (var id in targets)
            db.Notifications.Add(new Notification { UserId = id, Message = message, Link = link });
        await db.SaveChangesAsync();

        await hub.Clients.Users(targets).SendAsync("notify", new { message, link });
    }
}
