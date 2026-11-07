using System.Security.Claims;
using Help_DeskTicket_System.Models;

namespace Help_DeskTicket_System.Services;

public static class QueryExtensions
{
    /// <summary>Staff see every ticket; clients only see the tickets they opened.</summary>
    public static IQueryable<Ticket> VisibleTo(this IQueryable<Ticket> q, ClaimsPrincipal user)
    {
        if (user.IsStaff()) return q;
        var id = user.Id();
        return q.Where(t => t.CreatedById == id);
    }
}
