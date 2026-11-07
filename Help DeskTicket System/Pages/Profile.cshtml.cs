using System.ComponentModel.DataAnnotations;
using Help_DeskTicket_System.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Help_DeskTicket_System.Pages;

public class ProfileModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : PageModel
{
    [BindProperty, Required(ErrorMessage = V.Required), MaxLength(120, ErrorMessage = V.MaxLength), Display(Name = "Full name")] public string FullName { get; set; } = "";
    [BindProperty, Phone(ErrorMessage = V.Phone), Display(Name = "Phone")] public string? PhoneNumber { get; set; }
    public string Email { get; set; } = "";

    [BindProperty, DataType(DataType.Password), Display(Name = "Current password")] public string? CurrentPassword { get; set; }
    [BindProperty, MinLength(8, ErrorMessage = V.MinLength), DataType(DataType.Password), Display(Name = "New password")] public string? NewPassword { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var u = await users.GetUserAsync(User);
        if (u is null) return Challenge();
        FullName = u.FullName; PhoneNumber = u.PhoneNumber; Email = u.Email ?? "";
        return Page();
    }

    public async Task<IActionResult> OnPostProfileAsync()
    {
        var u = await users.GetUserAsync(User);
        if (u is null) return Challenge();
        ModelState.Remove(nameof(NewPassword)); ModelState.Remove(nameof(CurrentPassword));
        if (!ModelState.IsValid) { Email = u.Email ?? ""; return Page(); }
        u.FullName = FullName.Trim();
        u.PhoneNumber = PhoneNumber;
        await users.UpdateAsync(u);
        TempData["ok"] = Lang.T("Profile saved.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        var u = await users.GetUserAsync(User);
        if (u is null) return Challenge();
        if (string.IsNullOrEmpty(NewPassword)) { TempData["err"] = Lang.T("Enter a new password."); return RedirectToPage(); }

        var r = await users.HasPasswordAsync(u)
            ? await users.ChangePasswordAsync(u, CurrentPassword ?? "", NewPassword)
            : await users.AddPasswordAsync(u, NewPassword);
        if (!r.Succeeded) { TempData["err"] = string.Join(" ", r.Errors.Select(e => e.Description)); return RedirectToPage(); }
        await signIn.RefreshSignInAsync(u);
        TempData["ok"] = Lang.T("Password changed.");
        return RedirectToPage();
    }
}
