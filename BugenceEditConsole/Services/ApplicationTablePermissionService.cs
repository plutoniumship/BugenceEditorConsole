using System.Data;
using BugenceEditConsole.Data;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public static class ApplicationTablePermissionService
{
    public const string AccessAdmin = "Admin";
    public const string AccessViewOnly = "ViewOnly";
    public const string AccessNoAccess = "NoAccess";

    public readonly record struct EffectivePermission(bool CanView, bool CanManage, string EffectiveAccessLevel);

    public readonly record struct TeamSubject(string UserId, string Role);

    public readonly record struct UserPermissionWrite(string UserId, string AccessLevel);

    public static async Task EnsurePermissionsTableAsync(ApplicationDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ApplicationTablePermissions (
    Id TEXT PRIMARY KEY,
    OwnerUserId TEXT NOT NULL,
    ApplicationTableId TEXT NOT NULL,
    SubjectUserId TEXT NOT NULL,
    AccessLevel TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_ApplicationTablePermissions_Owner_Table_Subject
    ON ApplicationTablePermissions (OwnerUserId, ApplicationTableId, SubjectUserId);
CREATE INDEX IF NOT EXISTS IX_ApplicationTablePermissions_Owner_Subject
    ON ApplicationTablePermissions (OwnerUserId, SubjectUserId);");
            return;
        }

        await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ApplicationTablePermissions' AND xtype='U')
CREATE TABLE ApplicationTablePermissions (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NOT NULL,
    ApplicationTableId UNIQUEIDENTIFIER NOT NULL,
    SubjectUserId NVARCHAR(450) NOT NULL,
    AccessLevel NVARCHAR(32) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL
);
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_ApplicationTablePermissions_Owner_Table_Subject'
      AND object_id = OBJECT_ID('ApplicationTablePermissions')
)
CREATE UNIQUE INDEX IX_ApplicationTablePermissions_Owner_Table_Subject
    ON ApplicationTablePermissions (OwnerUserId, ApplicationTableId, SubjectUserId);
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_ApplicationTablePermissions_Owner_Subject'
      AND object_id = OBJECT_ID('ApplicationTablePermissions')
)
CREATE INDEX IX_ApplicationTablePermissions_Owner_Subject
    ON ApplicationTablePermissions (OwnerUserId, SubjectUserId);");
    }

    public static async Task CleanupOrphanedPermissionsAsync(ApplicationDbContext db, string ownerUserId)
    {
        await EnsurePermissionsTableAsync(db);

        var provider = db.Database.ProviderName ?? string.Empty;
        using var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            command.CommandText = @"
DELETE FROM ApplicationTablePermissions
WHERE OwnerUserId = @owner
  AND SubjectUserId <> @owner
  AND NOT EXISTS (
    SELECT 1
    FROM TeamMembers tm
    WHERE tm.OwnerUserId = @owner
      AND tm.UserId = ApplicationTablePermissions.SubjectUserId
      AND tm.UserId IS NOT NULL
  )";
        }
        else
        {
            command.CommandText = @"
DELETE FROM ApplicationTablePermissions
WHERE OwnerUserId = @owner
  AND SubjectUserId <> @owner
  AND NOT EXISTS (
    SELECT 1
    FROM TeamMembers tm
    WHERE tm.OwnerUserId = @owner
      AND tm.UserId = ApplicationTablePermissions.SubjectUserId
      AND tm.UserId IS NOT NULL
  )";
        }

        AddParameter(command, "@owner", ownerUserId);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task BackfillMissingPermissionsAsync(
        ApplicationDbContext db,
        string ownerUserId,
        IReadOnlyCollection<Guid> tableIds,
        IReadOnlyCollection<TeamSubject> members)
    {
        await EnsurePermissionsTableAsync(db);

        if (tableIds.Count == 0 || members.Count == 0)
        {
            return;
        }

        var provider = db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        using var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tableParams = tableIds.Select((_, idx) => $"@t{idx}").ToList();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT ApplicationTableId, SubjectUserId FROM ApplicationTablePermissions WHERE OwnerUserId = @owner AND ApplicationTableId IN ({string.Join(", ", tableParams)})";
            AddParameter(command, "@owner", ownerUserId);
            for (var i = 0; i < tableIds.Count; i++)
            {
                AddParameter(command, tableParams[i], tableIds.ElementAt(i));
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var tableId = isSqlite ? Guid.Parse(reader.GetString(0)) : reader.GetGuid(0);
                var subjectUserId = reader.GetString(1);
                existing.Add(BuildKey(tableId, subjectUserId));
            }
        }

        var now = DateTime.UtcNow;
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var tableId in tableIds)
        {
            foreach (var member in members)
            {
                var key = BuildKey(tableId, member.UserId);
                if (existing.Contains(key))
                {
                    continue;
                }

                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = @"
INSERT INTO ApplicationTablePermissions (Id, OwnerUserId, ApplicationTableId, SubjectUserId, AccessLevel, CreatedAtUtc, UpdatedAtUtc)
VALUES (@id, @owner, @tableId, @subject, @access, @created, @updated)";
                AddParameter(insert, "@id", Guid.NewGuid());
                AddParameter(insert, "@owner", ownerUserId);
                AddParameter(insert, "@tableId", tableId);
                AddParameter(insert, "@subject", member.UserId);
                AddParameter(insert, "@access", GetDefaultAccessForRole(member.Role));
                AddParameter(insert, "@created", now);
                AddParameter(insert, "@updated", now);
                await insert.ExecuteNonQueryAsync();

                existing.Add(key);
            }
        }

        await transaction.CommitAsync();
    }

    public static async Task<Dictionary<Guid, string>> GetExplicitPermissionsForUserAsync(
        ApplicationDbContext db,
        string ownerUserId,
        string subjectUserId,
        IReadOnlyCollection<Guid> tableIds)
    {
        await EnsurePermissionsTableAsync(db);

        var result = new Dictionary<Guid, string>();
        if (tableIds.Count == 0)
        {
            return result;
        }

        var provider = db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        using var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var tableParams = tableIds.Select((_, idx) => $"@t{idx}").ToList();
        await using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT ApplicationTableId, AccessLevel
FROM ApplicationTablePermissions
WHERE OwnerUserId = @owner
  AND SubjectUserId = @subject
  AND ApplicationTableId IN ({string.Join(", ", tableParams)})";
        AddParameter(command, "@owner", ownerUserId);
        AddParameter(command, "@subject", subjectUserId);
        for (var i = 0; i < tableIds.Count; i++)
        {
            AddParameter(command, tableParams[i], tableIds.ElementAt(i));
        }

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var tableId = isSqlite ? Guid.Parse(reader.GetString(0)) : reader.GetGuid(0);
            result[tableId] = NormalizeAccess(reader.GetString(1));
        }

        return result;
    }

    public static async Task<string?> GetExplicitPermissionAsync(
        ApplicationDbContext db,
        string ownerUserId,
        Guid tableId,
        string subjectUserId)
    {
        await EnsurePermissionsTableAsync(db);

        using var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT AccessLevel
FROM ApplicationTablePermissions
WHERE OwnerUserId = @owner
  AND ApplicationTableId = @tableId
  AND SubjectUserId = @subject";
        AddParameter(command, "@owner", ownerUserId);
        AddParameter(command, "@tableId", tableId);
        AddParameter(command, "@subject", subjectUserId);
        var obj = await command.ExecuteScalarAsync();
        return obj == null ? null : NormalizeAccess(Convert.ToString(obj));
    }

    public static async Task<Dictionary<string, string>> GetExplicitPermissionsForTableAsync(
        ApplicationDbContext db,
        string ownerUserId,
        Guid tableId)
    {
        await EnsurePermissionsTableAsync(db);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT SubjectUserId, AccessLevel
FROM ApplicationTablePermissions
WHERE OwnerUserId = @owner
  AND ApplicationTableId = @tableId";
        AddParameter(command, "@owner", ownerUserId);
        AddParameter(command, "@tableId", tableId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetString(0)] = NormalizeAccess(reader.GetString(1));
        }

        return result;
    }

    public static async Task UpsertPermissionsAsync(
        ApplicationDbContext db,
        string ownerUserId,
        Guid tableId,
        IReadOnlyCollection<UserPermissionWrite> writes)
    {
        await EnsurePermissionsTableAsync(db);

        using var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var now = DateTime.UtcNow;
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var write in writes)
        {
            var normalized = NormalizeAccess(write.AccessLevel);
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = @"
UPDATE ApplicationTablePermissions
SET AccessLevel = @access,
    UpdatedAtUtc = @updated
WHERE OwnerUserId = @owner
  AND ApplicationTableId = @tableId
  AND SubjectUserId = @subject";
            AddParameter(update, "@access", normalized);
            AddParameter(update, "@updated", now);
            AddParameter(update, "@owner", ownerUserId);
            AddParameter(update, "@tableId", tableId);
            AddParameter(update, "@subject", write.UserId);
            var affected = await update.ExecuteNonQueryAsync();

            if (affected > 0)
            {
                continue;
            }

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = @"
INSERT INTO ApplicationTablePermissions (Id, OwnerUserId, ApplicationTableId, SubjectUserId, AccessLevel, CreatedAtUtc, UpdatedAtUtc)
VALUES (@id, @owner, @tableId, @subject, @access, @created, @updated)";
            AddParameter(insert, "@id", Guid.NewGuid());
            AddParameter(insert, "@owner", ownerUserId);
            AddParameter(insert, "@tableId", tableId);
            AddParameter(insert, "@subject", write.UserId);
            AddParameter(insert, "@access", normalized);
            AddParameter(insert, "@created", now);
            AddParameter(insert, "@updated", now);
            await insert.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public static EffectivePermission ResolveEffectivePermission(
        string currentUserId,
        string ownerUserId,
        string? teamRole,
        string? explicitAccessLevel)
    {
        var effective = ResolveEffectiveAccessLevel(currentUserId, ownerUserId, teamRole, explicitAccessLevel);
        var canView = !string.Equals(effective, AccessNoAccess, StringComparison.OrdinalIgnoreCase);
        var canManage = string.Equals(effective, AccessAdmin, StringComparison.OrdinalIgnoreCase);
        return new EffectivePermission(canView, canManage, effective);
    }

    public static string ResolveEffectiveAccessLevel(
        string currentUserId,
        string ownerUserId,
        string? teamRole,
        string? explicitAccessLevel)
    {
        if (string.Equals(currentUserId, ownerUserId, StringComparison.Ordinal))
        {
            return AccessAdmin;
        }

        if (!string.IsNullOrWhiteSpace(explicitAccessLevel))
        {
            return NormalizeAccess(explicitAccessLevel);
        }

        if (string.IsNullOrWhiteSpace(teamRole))
        {
            return AccessNoAccess;
        }

        return GetDefaultAccessForRole(teamRole);
    }

    public static string GetDefaultAccessForRole(string? role)
    {
        return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
            ? AccessAdmin
            : AccessViewOnly;
    }

    public static string NormalizeAccess(string? accessLevel)
    {
        if (string.Equals(accessLevel, AccessAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return AccessAdmin;
        }

        if (string.Equals(accessLevel, AccessNoAccess, StringComparison.OrdinalIgnoreCase))
        {
            return AccessNoAccess;
        }

        return AccessViewOnly;
    }

    private static string BuildKey(Guid tableId, string subjectUserId)
        => $"{tableId:N}|{subjectUserId}";

    private static void AddParameter(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
