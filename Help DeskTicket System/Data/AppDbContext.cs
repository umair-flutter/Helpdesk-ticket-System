using Help_DeskTicket_System.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Help_DeskTicket_System.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Priority> Priorities => Set<Priority>();
    public DbSet<TicketStatus> Statuses => Set<TicketStatus>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketReply> Replies => Set<TicketReply>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<TicketActivity> Activities => Set<TicketActivity>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<CannedResponse> CannedResponses => Set<CannedResponse>();
    public DbSet<KbArticle> KbArticles => Set<KbArticle>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Ticket>(e =>
        {
            e.HasOne(t => t.CreatedBy).WithMany().HasForeignKey(t => t.CreatedById).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.AssignedTo).WithMany().HasForeignKey(t => t.AssignedToId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.Category).WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.Priority).WithMany().HasForeignKey(t => t.PriorityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Status).WithMany().HasForeignKey(t => t.StatusId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(t => t.Attachments).WithOne().HasForeignKey(a => a.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(t => t.Activities).WithOne().HasForeignKey(a => a.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(t => t.StatusId);
            e.HasIndex(t => t.AssignedToId);
            e.HasIndex(t => t.CreatedAt);
        });

        b.Entity<TicketReply>(e =>
        {
            e.HasOne(r => r.Ticket).WithMany(t => t.Replies).HasForeignKey(r => r.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Author).WithMany().HasForeignKey(r => r.AuthorId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(r => r.Attachments).WithOne().HasForeignKey(a => a.ReplyId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ApplicationUser>().HasOne(u => u.Department).WithMany().HasForeignKey(u => u.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Category>().HasOne(c => c.Department).WithMany().HasForeignKey(c => c.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<KbArticle>().HasOne(k => k.Category).WithMany().HasForeignKey(k => k.CategoryId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Notification>().HasIndex(n => new { n.UserId, n.IsRead });
    }
}
