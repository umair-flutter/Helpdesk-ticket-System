using Help_DeskTicket_System.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Help_DeskTicket_System.Pages.Admin;

public class SettingsModel(SettingsService settings, EmailService email, IServiceProvider sp) : PageModel
{
    public static readonly string[] SecretKeys = [SettingKeys.SmtpPassword, SettingKeys.ImapPassword];
    public static readonly string[] BoolKeys =
    [
        SettingKeys.AllowRegistration, SettingKeys.AutoAssign, SettingKeys.ClientsCanReopen,
        SettingKeys.SmtpEnabled, SettingKeys.SmtpUseSsl, SettingKeys.ImapEnabled, SettingKeys.ImapUseSsl
    ];
    public static readonly string[] TextKeys =
    [
        SettingKeys.SiteName, SettingKeys.AutoCloseDays,
        SettingKeys.SmtpHost, SettingKeys.SmtpPort, SettingKeys.SmtpUser, SettingKeys.SmtpFromEmail, SettingKeys.SmtpFromName,
        SettingKeys.ImapHost, SettingKeys.ImapPort, SettingKeys.ImapUser, SettingKeys.ImapPollMinutes
    ];

    public Dictionary<string, string?> Values { get; set; } = [];

    public string V(string key) => Values.GetValueOrDefault(key) ?? "";
    public bool B(string key) => bool.TryParse(Values.GetValueOrDefault(key), out var b) && b;
    public bool HasSecret(string key) => !string.IsNullOrEmpty(Values.GetValueOrDefault(key));

    public async Task OnGetAsync() => Values = await settings.GetAllAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        var form = Request.Form;
        var updates = new Dictionary<string, string?>();
        foreach (var k in TextKeys) updates[k] = form[k].ToString().Trim();
        foreach (var k in BoolKeys) updates[k] = form[k].Contains("true") ? "true" : "false";
        foreach (var k in SecretKeys)
        {
            var v = form[k].ToString();
            if (form[$"{k}.Clear"] == "true") updates[k] = null;
            else if (!string.IsNullOrEmpty(v)) updates[k] = v; // blank = keep existing secret
        }
        await settings.SetManyAsync(updates);
        TempData["ok"] = Lang.T("Settings saved.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestEmailAsync([FromServices] UserManager<Models.ApplicationUser> users)
    {
        var me = await users.GetUserAsync(User);
        try
        {
            await email.SendAsync(me?.Email, "Help desk test email", "<p>Your SMTP settings work. 🎉</p>", throwOnError: true);
            TempData["ok"] = Lang.T("Test email sent to {0}.", me?.Email);
        }
        catch (Exception ex) { TempData["err"] = Lang.T("Sending failed: {0}", ex.Message); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCheckInboxAsync()
    {
        try
        {
            var n = await EmailIngestionService.PollAsync(sp);
            TempData["ok"] = Lang.T("Inbox checked: {0} email(s) imported.", n);
        }
        catch (Exception ex) { TempData["err"] = Lang.T("Inbox check failed: {0}", ex.Message); }
        return RedirectToPage();
    }
}
