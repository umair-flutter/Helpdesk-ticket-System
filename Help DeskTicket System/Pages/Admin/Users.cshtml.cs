using System.ComponentModel.DataAnnotations;
using Help_DeskTicket_System.Data;
using Help_DeskTicket_System.Models;
using Help_DeskTicket_System.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Help_DeskTicket_System.Pages.Admin;

public class UsersModel(AppDbContext db, UserManager<ApplicationUser> users) : PageModel
{
    public record Row(ApplicationUser User, string Role, int OpenAssigned, int Requested);

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Role { get; set; }
    public List<Row> Rows { get; set; } = [];
    public List<Department> Departments { get; set; } = [];

    public class Input
    {
        public string? Id { get; set; }
        [Required(ErrorMessage = V.Required), MaxLength(120, ErrorMessage = V.MaxLength), Display(Name = "Full name")] public string FullName { get; set; } = "";
        [Required(ErrorMessage = V.Required), EmailAddress(ErrorMessage = V.Email), Display(Name = "Email")] public string Email { get; set; } = "";
        [Required(ErrorMessage = V.Required)] public string Role { get; set; } = Roles.Client;
        public int? DepartmentId { get; set; }
        public bool IsActive { get; set; } = true;
        [MinLength(8, ErrorMessage = V.MinLength), Display(Name = "Password")] public string? Password { get; set; }
    }
    [BindProperty] public Input Form { get; set; } = new();

    public async Task OnGetAsync()
    {
        Departments = await db.Departments.OrderBy(d => d.Name).ToListAsync();
        var q = db.Users.Include(u => u.Department).AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q)) q = q.Where(u => u.FullName.Contains(Q) || u.Email!.Contains(Q));
        var list = await q.OrderBy(u => u.FullName).Take(500).ToListAsync();

        var roleMap = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id select new { ur.UserId, r.Name })
            .ToDictionaryAsync(x => x.UserId, x => x.Name!);
        var open = await db.Tickets.Where(t => t.AssignedToId != null && !t.Status!.IsClosed)
            .GroupBy(t => t.AssignedToId!).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);
        var requested = await db.Tickets.Where(t => t.CreatedById != null)
            .GroupBy(t => t.CreatedById!).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C);

        Rows = list.Select(u => new Row(u, roleMap.GetValueOrDefault(u.Id, "—"), open.GetValueOrDefault(u.Id), requested.GetValueOrDefault(u.Id)))
            .Where(r => string.IsNullOrEmpty(Role) || r.Role == Role).ToList();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!Roles.All.Contains(Form.Role)) ModelState.AddModelError("", Lang.T("Invalid role."));
        if (!ModelState.IsValid) { TempData["err"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)); return RedirectToPage(); }

        ApplicationUser? u;
        IdentityResult r;
        if (string.IsNullOrEmpty(Form.Id))
        {
            if (string.IsNullOrEmpty(Form.Password)) { TempData["err"] = Lang.T("A password is required for new users."); return RedirectToPage(); }
            u = new ApplicationUser { UserName = Form.Email, Email = Form.Email, EmailConfirmed = true };
            Apply(u);
            r = await users.CreateAsync(u, Form.Password);
        }
        else
        {
            u = await users.FindByIdAsync(Form.Id);
            if (u is null) return NotFound();
            if (u.Id == User.Id() && (!Form.IsActive || Form.Role != Roles.Admin))
            {
                TempData["err"] = Lang.T("You can't deactivate or demote your own admin account.");
                return RedirectToPage();
            }
            Apply(u);
            u.Email = u.UserName = Form.Email;
            r = await users.UpdateAsync(u);
            if (r.Succeeded && !string.IsNullOrEmpty(Form.Password))
            {
                var token = await users.GeneratePasswordResetTokenAsync(u);
                r = await users.ResetPasswordAsync(u, token, Form.Password);
            }
            if (r.Succeeded && !u.IsActive) await users.UpdateSecurityStampAsync(u); // signs them out
        }
        if (!r.Succeeded) { TempData["err"] = string.Join(" ", r.Errors.Select(e => e.Description)); return RedirectToPage(); }

        var current = await users.GetRolesAsync(u);
        if (!current.SequenceEqual([Form.Role]))
        {
            await users.RemoveFromRolesAsync(u, current);
            await users.AddToRoleAsync(u, Form.Role);
        }
        TempData["ok"] = Lang.T("Saved {0}.", u.Email);
        return RedirectToPage();
    }

    private void Apply(ApplicationUser u)
    {
        u.FullName = Form.FullName.Trim();
        u.DepartmentId = Form.DepartmentId;
        u.IsActive = Form.IsActive;
    }
}
