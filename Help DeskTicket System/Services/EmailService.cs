using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Help_DeskTicket_System.Services;

/// <summary>Sends outgoing mail using the SMTP settings an admin configured in the app.</summary>
public class EmailService(SettingsService settings, ILogger<EmailService> log)
{
    public async Task<bool> SendAsync(string? to, string subject, string htmlBody, bool throwOnError = false)
    {
        if (string.IsNullOrWhiteSpace(to)) return false;
        var s = await settings.GetAllAsync();
        if (!bool.TryParse(s.GetValueOrDefault(SettingKeys.SmtpEnabled), out var enabled) || !enabled)
        {
            if (throwOnError) throw new InvalidOperationException("Outgoing email (SMTP) is disabled in Settings.");
            return false;
        }

        try
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(s.GetValueOrDefault(SettingKeys.SmtpFromName) ?? "Help Desk",
                                            s.GetValueOrDefault(SettingKeys.SmtpFromEmail) ?? s.GetValueOrDefault(SettingKeys.SmtpUser) ?? ""));
            msg.To.Add(MailboxAddress.Parse(to));
            msg.Subject = subject;
            msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            using var client = new SmtpClient();
            var port = int.TryParse(s.GetValueOrDefault(SettingKeys.SmtpPort), out var p) ? p : 587;
            var ssl = bool.TryParse(s.GetValueOrDefault(SettingKeys.SmtpUseSsl), out var x) && x;
            await client.ConnectAsync(s.GetValueOrDefault(SettingKeys.SmtpHost) ?? "", port,
                ssl ? (port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls) : SecureSocketOptions.Auto);
            var user = s.GetValueOrDefault(SettingKeys.SmtpUser);
            if (!string.IsNullOrEmpty(user))
                await client.AuthenticateAsync(user, s.GetValueOrDefault(SettingKeys.SmtpPassword) ?? "");
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
            return true;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to send email to {To}", to);
            if (throwOnError) throw;
            return false;
        }
    }
}
