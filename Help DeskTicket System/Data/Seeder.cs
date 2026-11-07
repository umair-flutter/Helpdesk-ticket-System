using Help_DeskTicket_System.Models;
using Help_DeskTicket_System.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Help_DeskTicket_System.Data;

public static class Seeder
{
    public static async Task SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var cfg = sp.GetRequiredService<IConfiguration>();
        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();

        await db.Database.EnsureCreatedAsync();

        foreach (var r in Roles.All)
            if (!await roles.RoleExistsAsync(r)) await roles.CreateAsync(new IdentityRole(r));

        if (!await db.Departments.AnyAsync())
        {
            db.Departments.AddRange(
                new Department { Name = "Technical Support", Description = "Product issues and bugs" },
                new Department { Name = "Billing", Description = "Invoices, payments and refunds" },
                new Department { Name = "IT Helpdesk", Description = "Internal IT requests" },
                new Department { Name = "Human Resources", Description = "HR and people requests" });
            await db.SaveChangesAsync();
        }

        if (!await db.Categories.AnyAsync())
        {
            var d = await db.Departments.ToDictionaryAsync(x => x.Name, x => x.Id);
            db.Categories.AddRange(
                new Category { Name = "Bug report", DepartmentId = d["Technical Support"] },
                new Category { Name = "How-to question", DepartmentId = d["Technical Support"] },
                new Category { Name = "Feature request", DepartmentId = d["Technical Support"] },
                new Category { Name = "Invoice / Payment", DepartmentId = d["Billing"] },
                new Category { Name = "Refund", DepartmentId = d["Billing"] },
                new Category { Name = "Hardware", DepartmentId = d["IT Helpdesk"] },
                new Category { Name = "Access / Password", DepartmentId = d["IT Helpdesk"] },
                new Category { Name = "Leave & Payroll", DepartmentId = d["Human Resources"] });
        }

        if (!await db.Priorities.AnyAsync())
            db.Priorities.AddRange(
                new Priority { Name = "Low", Color = "#6c757d", SortOrder = 1, SlaResponseHours = 48, SlaResolveHours = 168 },
                new Priority { Name = "Medium", Color = "#0d6efd", SortOrder = 2, SlaResponseHours = 24, SlaResolveHours = 72, IsDefault = true },
                new Priority { Name = "High", Color = "#fd7e14", SortOrder = 3, SlaResponseHours = 8, SlaResolveHours = 24 },
                new Priority { Name = "Urgent", Color = "#dc3545", SortOrder = 4, SlaResponseHours = 1, SlaResolveHours = 8 });

        if (!await db.Statuses.AnyAsync())
            db.Statuses.AddRange(
                new TicketStatus { Name = "Open", Color = "#0d6efd", SortOrder = 1, IsDefault = true },
                new TicketStatus { Name = "In Progress", Color = "#6f42c1", SortOrder = 2 },
                new TicketStatus { Name = "Waiting on Customer", Color = "#ffc107", SortOrder = 3 },
                new TicketStatus { Name = "Resolved", Color = "#198754", SortOrder = 4, IsClosed = true },
                new TicketStatus { Name = "Closed", Color = "#343a40", SortOrder = 5, IsClosed = true });

        if (!await db.Settings.AnyAsync())
            db.Settings.AddRange(new Dictionary<string, string>
            {
                [SettingKeys.SiteName] = "HelpDesk",
                [SettingKeys.AllowRegistration] = "true",
                [SettingKeys.AutoAssign] = "true",
                [SettingKeys.ClientsCanReopen] = "true",
                [SettingKeys.AutoCloseDays] = "7",
                [SettingKeys.SmtpEnabled] = "false",
                [SettingKeys.SmtpPort] = "587",
                [SettingKeys.SmtpUseSsl] = "true",
                [SettingKeys.SmtpFromName] = "HelpDesk Support",
                [SettingKeys.ImapEnabled] = "false",
                [SettingKeys.ImapPort] = "993",
                [SettingKeys.ImapUseSsl] = "true",
                [SettingKeys.ImapPollMinutes] = "5",
            }.Select(kv => new AppSetting { Key = kv.Key, Value = kv.Value }));

        if (!await db.CannedResponses.AnyAsync())
            db.CannedResponses.AddRange(
                new CannedResponse { Title = "Greeting", Body = "Hi,\n\nThanks for contacting support. I'm looking into this now and will update you shortly." },
                new CannedResponse { Title = "Need more info", Body = "Could you please share a screenshot and the exact steps to reproduce the problem?" },
                new CannedResponse { Title = "Resolved", Body = "This should now be fixed. I'll mark the ticket resolved — just reply if anything is still wrong." });

        await db.SaveChangesAsync();

        if (!await db.KbArticles.AnyAsync())
        {
            var access = await db.Categories.FirstOrDefaultAsync(c => c.Name == "Access / Password");
            var invoice = await db.Categories.FirstOrDefaultAsync(c => c.Name == "Invoice / Payment");
            db.KbArticles.AddRange(
                new KbArticle { Title = "How do I reset my password?", CategoryId = access?.Id,
                    Body = "1. Open the sign-in page.\n2. Use your email and a temporary password provided by support, or ask an agent to reset it.\n3. After signing in, go to **My Profile → Change password**." },
                new KbArticle { Title = "Where can I download my invoice?", CategoryId = invoice?.Id,
                    Body = "Invoices are emailed after each payment. If you can't find one, open a ticket in the **Billing** department with the payment date and amount." },
                new KbArticle { Title = "How to write a ticket that gets solved fast",
                    Body = "- Use a clear subject\n- Describe what you expected vs. what happened\n- Add screenshots or log files as attachments\n- Choose the right department and priority" });
            await db.SaveChangesAsync();
        }

        // Starter accounts — credentials come from configuration (appsettings.Development.json → "Seed").
        foreach (var section in cfg.GetSection("Seed:Users").GetChildren())
        {
            var mail = section["Email"];
            var pass = section["Password"];
            var role = section["Role"] ?? Roles.Client;
            if (string.IsNullOrEmpty(mail) || string.IsNullOrEmpty(pass) || await users.FindByEmailAsync(mail) is not null) continue;

            var deptName = section["Department"];
            var dept = deptName is null ? null : await db.Departments.FirstOrDefaultAsync(d => d.Name == deptName);
            var u = new ApplicationUser { UserName = mail, Email = mail, EmailConfirmed = true, FullName = section["FullName"] ?? mail, DepartmentId = dept?.Id };
            if ((await users.CreateAsync(u, pass)).Succeeded) await users.AddToRoleAsync(u, role);
        }
    }
}
