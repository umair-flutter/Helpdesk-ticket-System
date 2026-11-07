using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Help_DeskTicket_System.Models;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Agent = "Agent";
    public const string Client = "Client";
    public static readonly string[] All = [Admin, Agent, Client];
}

public class ApplicationUser : IdentityUser
{
    [MaxLength(120)] public string FullName { get; set; } = "";
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? (Email ?? UserName ?? "") : FullName;
}

public class Department
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(300)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Category
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Priority
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string Name { get; set; } = "";
    [MaxLength(20)] public string Color { get; set; } = "#6c757d";
    public int SortOrder { get; set; }
    /// <summary>Hours allowed before the first agent reply is due.</summary>
    public int SlaResponseHours { get; set; } = 24;
    /// <summary>Hours allowed before the ticket must be resolved.</summary>
    public int SlaResolveHours { get; set; } = 72;
    public bool IsDefault { get; set; }
}

public class TicketStatus
{
    public int Id { get; set; }
    [Required, MaxLength(50)] public string Name { get; set; } = "";
    [MaxLength(20)] public string Color { get; set; } = "#6c757d";
    public int SortOrder { get; set; }
    /// <summary>Status given to new tickets.</summary>
    public bool IsDefault { get; set; }
    /// <summary>Tickets in this status count as resolved/closed (stops SLA clock).</summary>
    public bool IsClosed { get; set; }
}

public enum TicketSource { Web, Email, Agent }

public class Ticket
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Subject { get; set; } = "";
    [Required] public string Description { get; set; } = "";

    public string? CreatedById { get; set; }
    public ApplicationUser? CreatedBy { get; set; }
    public string? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }

    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public int PriorityId { get; set; }
    public Priority? Priority { get; set; }
    public int StatusId { get; set; }
    public TicketStatus? Status { get; set; }

    public TicketSource Source { get; set; } = TicketSource.Web;
    [MaxLength(200)] public string? Tags { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FirstResponseAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ResponseDueAt { get; set; }
    public DateTime? ResolveDueAt { get; set; }
    public bool OverdueNotified { get; set; }

    /// <summary>False until a staff member opens it — drives the "new" highlight.</summary>
    public bool SeenByStaff { get; set; }

    [Range(1, 5)] public int? Rating { get; set; }
    [MaxLength(1000)] public string? RatingComment { get; set; }

    public List<TicketReply> Replies { get; set; } = [];
    public List<Attachment> Attachments { get; set; } = [];
    public List<TicketActivity> Activities { get; set; } = [];

    public string Number => $"HD-{Id:D5}";
    public bool IsOverdue => Status is { IsClosed: false } && ResolveDueAt.HasValue && ResolveDueAt < DateTime.UtcNow;
}

public class TicketReply
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public string? AuthorId { get; set; }
    public ApplicationUser? Author { get; set; }
    [Required] public string Body { get; set; } = "";
    /// <summary>Internal notes are only visible to agents and admins.</summary>
    public bool IsInternal { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Attachment> Attachments { get; set; } = [];
}

public class Attachment
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public int? ReplyId { get; set; }
    [MaxLength(255)] public string FileName { get; set; } = "";
    [MaxLength(100)] public string StoredName { get; set; } = "";
    [MaxLength(150)] public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class TicketActivity
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    [MaxLength(500)] public string Action { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Notification
{
    public int Id { get; set; }
    [Required] public string UserId { get; set; } = "";
    [MaxLength(300)] public string Message { get; set; } = "";
    [MaxLength(300)] public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CannedResponse
{
    public int Id { get; set; }
    [Required, MaxLength(120)] public string Title { get; set; } = "";
    [Required] public string Body { get; set; } = "";
}

public class KbArticle
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [Required] public string Body { get; set; } = "";
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public bool IsPublished { get; set; } = true;
    public int Views { get; set; }
    public int HelpfulYes { get; set; }
    public int HelpfulNo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AppSetting
{
    [Key, MaxLength(100)] public string Key { get; set; } = "";
    public string? Value { get; set; }
}
