using Help_DeskTicket_System.Data;
using Help_DeskTicket_System.Models;
using Microsoft.EntityFrameworkCore;

namespace Help_DeskTicket_System.Services;

/// <summary>Key/value settings stored in the database so admins can change them at runtime.</summary>
public static class SettingKeys
{
    public const string SiteName = "General.SiteName";
    public const string AllowRegistration = "General.AllowRegistration";
    public const string AutoAssign = "Tickets.AutoAssign";              // true/false
    public const string ClientsCanReopen = "Tickets.ClientsCanReopen";
    public const string AutoCloseDays = "Tickets.AutoCloseDays";        // resolved tickets auto-close after N days (0 = off)

    public const string SmtpEnabled = "Smtp.Enabled";
    public const string SmtpHost = "Smtp.Host";
    public const string SmtpPort = "Smtp.Port";
    public const string SmtpUser = "Smtp.User";
    public const string SmtpPassword = "Smtp.Password";
    public const string SmtpUseSsl = "Smtp.UseSsl";
    public const string SmtpFromEmail = "Smtp.FromEmail";
    public const string SmtpFromName = "Smtp.FromName";

    public const string ImapEnabled = "Imap.Enabled";
    public const string ImapHost = "Imap.Host";
    public const string ImapPort = "Imap.Port";
    public const string ImapUser = "Imap.User";
    public const string ImapPassword = "Imap.Password";
    public const string ImapUseSsl = "Imap.UseSsl";
    public const string ImapPollMinutes = "Imap.PollMinutes";
}

public class SettingsService(AppDbContext db)
{
    public async Task<Dictionary<string, string?>> GetAllAsync() =>
        await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);

    public async Task<string?> GetAsync(string key, string? fallback = null) =>
        (await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key))?.Value ?? fallback;

    public async Task<bool> GetBoolAsync(string key, bool fallback = false) =>
        bool.TryParse(await GetAsync(key), out var v) ? v : fallback;

    public async Task<int> GetIntAsync(string key, int fallback = 0) =>
        int.TryParse(await GetAsync(key), out var v) ? v : fallback;

    public async Task SetManyAsync(IDictionary<string, string?> values)
    {
        foreach (var (key, value) in values)
        {
            var s = await db.Settings.FindAsync(key);
            if (s is null) db.Settings.Add(new AppSetting { Key = key, Value = value });
            else s.Value = value;
        }
        await db.SaveChangesAsync();
    }
}
