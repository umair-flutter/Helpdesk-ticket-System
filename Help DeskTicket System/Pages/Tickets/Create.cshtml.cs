using System.ComponentModel.DataAnnotations;
using Help_DeskTicket_System.Data;
using Help_DeskTicket_System.Models;
using Help_DeskTicket_System.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Help_DeskTicket_System.Pages.Tickets;

public class CreateModel(AppDbContext db, TicketService tickets, UserManager<ApplicationUser> users) : PageModel
{
    public class Input
    {
        [Required(ErrorMessage = V.Required), MaxLength(200, ErrorMessage = V.MaxLength), Display(Name = "Subject")] public string Subject { get; set; } = "";
        [Required(ErrorMessage = V.Required), Display(Name = "Describe the problem")] public string Description { get; set; } = "";
        [Display(Name = "Department")] public int? DepartmentId { get; set; }
        [Display(Name = "Category")] public int? CategoryId { get; set; }
        [Display(Name = "Priority")] public int PriorityId { get; set; }
        [MaxLength(200, ErrorMessage = V.MaxLength)] public string? Tags { get; set; }
        /// <summary>Staff only: open the ticket on behalf of a customer.</summary>
        [EmailAddress(ErrorMessage = V.Email), Display(Name = "Requester email (optional)")] public string? RequesterEmail { get; set; }
        [Display(Name = "Assign to")] public string? AssignedToId { get; set; }
    }

    [BindProperty] public Input Form { get; set; } = new();
    [BindProperty] public List<IFormFile> Files { get; set; } = [];

    public List<Department> Departments { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
    public List<Priority> Priorities { get; set; } = [];
    public List<ApplicationUser> Agents { get; set; } = [];

    public async Task OnGetAsync()
    {
        await LoadAsync();
        Form.PriorityId = Priorities.FirstOrDefault(p => p.IsDefault)?.Id ?? Priorities.FirstOrDefault()?.Id ?? 0;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Files.Any(f => f.Length > TicketService.MaxFileBytes))
            ModelState.AddModelError("Files", Lang.T("Each attachment must be 10 MB or smaller."));
        if (Files.Any(f => !TicketService.AllowedExtensions.Contains(Path.GetExtension(f.FileName).ToLowerInvariant())))
            ModelState.AddModelError("Files", Lang.T("One of the attachments has a file type that isn't allowed."));
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }

        var creatorId = User.Id();
        var source = TicketSource.Web;
        if (User.IsStaff() && !string.IsNullOrWhiteSpace(Form.RequesterEmail))
        {
            var requester = await users.FindByEmailAsync(Form.RequesterEmail);
            if (requester is null)
            {
                requester = new ApplicationUser { UserName = Form.RequesterEmail, Email = Form.RequesterEmail, FullName = Form.RequesterEmail };
                var r = await users.CreateAsync(requester);
                if (!r.Succeeded) { ModelState.AddModelError("", string.Join(" ", r.Errors.Select(e => e.Description))); await LoadAsync(); return Page(); }
                await users.AddToRoleAsync(requester, Roles.Client);
            }
            creatorId = requester.Id;
            source = TicketSource.Agent;
        }

        var t = await tickets.CreateAsync(new Ticket
        {
            Subject = Form.Subject.Trim(),
            Description = Form.Description,
            DepartmentId = Form.DepartmentId,
            CategoryId = Form.CategoryId,
            PriorityId = Form.PriorityId,
            Tags = User.IsStaff() ? Form.Tags : null,
            CreatedById = creatorId,
            AssignedToId = User.IsStaff() && !string.IsNullOrEmpty(Form.AssignedToId) ? Form.AssignedToId : null,
            Source = source,
            SeenByStaff = User.IsStaff()
        }, User.Id(), TicketService.FromForm(Files));

        TempData["ok"] = Lang.T("Ticket {0} created.", t.Number);
        return RedirectToPage("Details", new { id = t.Id });
    }

    private async Task LoadAsync()
    {
        Departments = await db.Departments.Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync();
        Categories = await db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        Priorities = await db.Priorities.OrderBy(p => p.SortOrder).ToListAsync();
        if (User.IsStaff())
            Agents = (await users.GetUsersInRoleAsync(Roles.Agent)).Concat(await users.GetUsersInRoleAsync(Roles.Admin))
                .Where(u => u.IsActive).DistinctBy(u => u.Id).OrderBy(u => u.DisplayName).ToList();
    }
}
