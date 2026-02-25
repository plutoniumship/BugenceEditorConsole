using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public static class CompanyAccessScopeResolver
{
    public sealed record Scope(
        string OwnerUserId,
        string OwnerEmail,
        string? TeamRole,
        bool IsOwner,
        bool CanManage,
        Guid? CompanyId);

    public static async Task<Scope> ResolveAsync(ApplicationDbContext db, ApplicationUser user)
    {
        var candidateMember = await db.TeamMembers
            .AsNoTracking()
            .Where(m => m.UserId == user.Id && m.Status == "Active")
            .OrderByDescending(m => m.JoinedAtUtc)
            .FirstOrDefaultAsync();

        TeamMember? member = null;
        if (candidateMember is not null)
        {
            var owner = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == candidateMember.OwnerUserId)
                .Select(u => new { u.Id, u.CompanyId })
                .FirstOrDefaultAsync();

            if (owner is not null && owner.CompanyId == user.CompanyId)
            {
                member = candidateMember;
            }
        }

        var ownerUserId = member?.OwnerUserId ?? user.Id;
        var teamRole = member?.Role;
        var isOwner = string.Equals(ownerUserId, user.Id, StringComparison.Ordinal);

        if (user.CompanyId.HasValue)
        {
            var companyUsers = await db.Users
                .AsNoTracking()
                .Where(u => u.CompanyId == user.CompanyId)
                .ToListAsync();

            var companyOwner = companyUsers
                .OrderByDescending(u => u.IsCompanyAdmin)
                .ThenBy(u => string.Equals(u.Email, "admin@bugence.com", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(u => u.Email)
                .FirstOrDefault();

            if (companyOwner != null)
            {
                ownerUserId = companyOwner.Id;
                isOwner = string.Equals(ownerUserId, user.Id, StringComparison.Ordinal);
                if (string.IsNullOrWhiteSpace(teamRole) && !isOwner)
                {
                    teamRole = user.IsCompanyAdmin ? "Admin" : "Viewer";
                }
            }
        }

        var ownerEmail = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == ownerUserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync() ?? "—";

        var canManage = isOwner
            || user.IsCompanyAdmin
            || string.Equals(teamRole, "Admin", StringComparison.OrdinalIgnoreCase);

        return new Scope(
            OwnerUserId: ownerUserId,
            OwnerEmail: ownerEmail,
            TeamRole: teamRole,
            IsOwner: isOwner,
            CanManage: canManage,
            CompanyId: user.CompanyId);
    }
}
