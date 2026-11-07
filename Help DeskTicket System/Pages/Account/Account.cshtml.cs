using System.ComponentModel.DataAnnotations;
using Help_DeskTicket_System.Models;
using Help_DeskTicket_System.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Help_DeskTicket_System.Pages.Account;

public class LoginModel(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, SettingsService settings) : PageModel
{
    [BindProperty, Required(ErrorMessage = V.Required), EmailAddress(ErrorMessage = V.Email), Display(Name = "Email")] public string Email { get; set; } = "";
    [BindProperty, Required(ErrorMessage = V.Required), DataType(DataType.Password), Display(Name = "Password")] public string Password { get; set; } = "";
    [BindProperty] public bool RememberMe { get; set; }
    public bool AllowRegistration { get; set; }

    public async Task OnGetAsync() => AllowRegistration = await settings.GetBoolAsync(SettingKeys.AllowRegistration, true);

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        AllowRegistration = await settings.GetBoolAsync(SettingKeys.AllowRegistration, true);
        if (!ModelState.IsValid) return Page();

        var user = await users.FindByEmailAsync(Email);
        if (user is { IsActive: false })
        {
            ModelState.AddModelError("", Lang.T("This account has been deactivated. Contact an administrator."));
            return Page();
        }
        var result = user is null ? Microsoft.AspNetCore.Identity.SignInResult.Failed : await signIn.PasswordSignInAsync(user, Password, RememberMe, lockoutOnFailure: true);
        if (result.Succeeded) return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
        ModelState.AddModelError("", Lang.T(result.IsLockedOut ? "Too many attempts. Try again in a few minutes." : "Invalid email or password."));
        return Page();
    }
}

public class RegisterModel(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, SettingsService settings) : PageModel
{
    [BindProperty, Required(ErrorMessage = V.Required), MaxLength(120, ErrorMessage = V.MaxLength), Display(Name = "Full name")] public string FullName { get; set; } = "";
    [BindProperty, Required(ErrorMessage = V.Required), EmailAddress(ErrorMessage = V.Email), Display(Name = "Email")] public string Email { get; set; } = "";
    [BindProperty, Required(ErrorMessage = V.Required), MinLength(8, ErrorMessage = V.MinLength), DataType(DataType.Password), Display(Name = "Password")] public string Password { get; set; } = "";
    [BindProperty, Compare(nameof(Password), ErrorMessage = V.Compare), DataType(DataType.Password), Display(Name = "Confirm password")] public string ConfirmPassword { get; set; } = "";

    public async Task<IActionResult> OnGetAsync() =>
        await settings.GetBoolAsync(SettingKeys.AllowRegistration, true) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await settings.GetBoolAsync(SettingKeys.AllowRegistration, true)) return NotFound();
        if (!ModelState.IsValid) return Page();

        var user = new ApplicationUser { UserName = Email, Email = Email, FullName = FullName };
        var result = await users.CreateAsync(user, Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            return Page();
        }
        await users.AddToRoleAsync(user, Roles.Client);
        await signIn.SignInAsync(user, isPersistent: false);
        return RedirectToPage("/Index");
    }
}

public class LogoutModel(SignInManager<ApplicationUser> signIn) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");
    public async Task<IActionResult> OnPostAsync()
    {
        await signIn.SignOutAsync();
        return RedirectToPage("/Account/Login");
    }
}

public class AccessDeniedModel : PageModel;

/// <summary>Sets the language cookie and returns to the page the user was on.</summary>
public class LanguageModel : PageModel
{
    public IActionResult OnGet(string culture, string? returnUrl)
    {
        if (Lang.Languages.Any(l => l.Code == culture))
            Response.Cookies.Append(Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.DefaultCookieName,
                Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.MakeCookieValue(new(culture)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax });
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
