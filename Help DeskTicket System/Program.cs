using Help_DeskTicket_System.Data;
using Help_DeskTicket_System.Models;
using Help_DeskTicket_System.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.LogoutPath = "/Account/Logout";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.SlidingExpiration = true;
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Staff", p => p.RequireRole(Roles.Admin, Roles.Agent))
    .AddPolicy("Admin", p => p.RequireRole(Roles.Admin));

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/");
    o.Conventions.AuthorizeFolder("/Admin", "Admin");
    o.Conventions.AuthorizePage("/Reports", "Staff");
    o.Conventions.AuthorizePage("/Kb/Edit", "Staff");
    o.Conventions.AllowAnonymousToFolder("/Account");
    o.Conventions.AllowAnonymousToPage("/Kb/Index");
    o.Conventions.AllowAnonymousToPage("/Kb/Article");
    o.Conventions.AllowAnonymousToPage("/Error");
}).AddDataAnnotationsLocalization(o => o.DataAnnotationLocalizerProvider = (_, f) => f.Create(typeof(Lang)));
builder.Services.AddSignalR();

// Localization: 16 languages from Resources/Lang/*.json, RTL for Arabic/Urdu/Persian.
Lang.Init(builder.Environment.ContentRootPath);
builder.Services.AddSingleton<Microsoft.Extensions.Localization.IStringLocalizerFactory, LangFactory>();
builder.Services.AddSingleton<Lang>();
// Emit non-Latin scripts as-is instead of &#x...; entities (still HTML-safe).
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o =>
    o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    var cultures = Lang.Cultures();
    o.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture(cultures[0]);
    o.SupportedCultures = cultures;
    o.SupportedUICultures = cultures;
    o.FallBackToParentCultures = true;
    o.FallBackToParentUICultures = true;
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 50 * 1024 * 1024);

builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<TicketService>();
builder.Services.AddHostedService<EmailIngestionService>();
builder.Services.AddHostedService<SlaMonitorService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await Seeder.SeedAsync(scope.ServiceProvider);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRequestLocalization();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();
