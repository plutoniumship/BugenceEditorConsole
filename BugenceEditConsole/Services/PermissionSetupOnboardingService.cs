using System.Data;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public static class PermissionSetupOnboardingService
{
    public static async Task<bool> RequiresSetupAsync(ApplicationDbContext db, ApplicationUser user)
    {
        var member = await db.TeamMembers
            .AsNoTracking()
            .Join(
                db.Users.AsNoTracking(),
                teamMember => teamMember.OwnerUserId,
                ownerUser => ownerUser.Id,
                (teamMember, ownerUser) => new { TeamMember = teamMember, OwnerCompanyId = ownerUser.CompanyId })
            .Where(x =>
                x.TeamMember.UserId == user.Id &&
                x.TeamMember.Status == "Active" &&
                x.OwnerCompanyId == user.CompanyId)
            .OrderByDescending(x => x.TeamMember.JoinedAtUtc)
            .Select(x => x.TeamMember)
            .FirstOrDefaultAsync();

        if (member != null)
        {
            // Team members don't own workspace-level permission setup.
            return false;
        }

        var subscription = await db.UserSubscriptions
            .AsNoTracking()
            .Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.UpdatedAtUtc)
            .FirstOrDefaultAsync();

        if (subscription == null || !string.Equals(subscription.PlanKey, "Starter", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var activeMembers = await db.TeamMembers
            .AsNoTracking()
            .Join(
                db.Users.AsNoTracking(),
                teamMember => teamMember.UserId!,
                memberUser => memberUser.Id,
                (teamMember, memberUser) => new { TeamMember = teamMember, MemberCompanyId = memberUser.CompanyId })
            .CountAsync(x =>
                x.TeamMember.OwnerUserId == user.Id &&
                x.TeamMember.Status == "Active" &&
                x.MemberCompanyId == user.CompanyId);

        if (activeMembers <= 0)
        {
            return false;
        }

        await EnsureTableAsync(db);
        using var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM PermissionSetupOnboarding WHERE OwnerUserId = @owner";
        AddParameter(command, "@owner", user.Id);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) <= 0;
    }

    public static async Task MarkCompletedAsync(ApplicationDbContext db, string ownerUserId)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            return;
        }

        await EnsureTableAsync(db);
        using var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE PermissionSetupOnboarding SET CompletedAtUtc = @completed WHERE OwnerUserId = @owner";
        AddParameter(update, "@completed", DateTime.UtcNow);
        AddParameter(update, "@owner", ownerUserId);
        var affected = await update.ExecuteNonQueryAsync();
        if (affected > 0)
        {
            return;
        }

        await using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO PermissionSetupOnboarding (OwnerUserId, CompletedAtUtc) VALUES (@owner, @completed)";
        AddParameter(insert, "@owner", ownerUserId);
        AddParameter(insert, "@completed", DateTime.UtcNow);
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task EnsureTableAsync(ApplicationDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS PermissionSetupOnboarding (
    OwnerUserId TEXT PRIMARY KEY,
    CompletedAtUtc TEXT NOT NULL
);");
            return;
        }

        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='PermissionSetupOnboarding' AND xtype='U')
CREATE TABLE PermissionSetupOnboarding (
    OwnerUserId NVARCHAR(450) NOT NULL PRIMARY KEY,
    CompletedAtUtc DATETIME2 NOT NULL
);");
    }

    private static void AddParameter(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
