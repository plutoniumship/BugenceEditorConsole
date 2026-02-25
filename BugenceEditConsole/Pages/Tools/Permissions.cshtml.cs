using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Tools;

[Authorize]
public class PermissionsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DebugPanelLogService _debugLogService;

    public PermissionsModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        DebugPanelLogService debugLogService)
    {
        _db = db;
        _userManager = userManager;
        _debugLogService = debugLogService;
    }

    public string UserName { get; private set; } = "Administrator";
    public string UserEmail { get; private set; } = "-";
    public string UserInitials { get; private set; } = "AD";
    public bool CanManage { get; private set; }
    public List<PermissionRecordRow> Records { get; private set; } = new();
    public string RecordsJson { get; private set; } = "[]";

    public async Task<IActionResult> OnGetAsync()
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return RedirectToPage("/Auth/Login");
        }

        UserName = context.User.GetFriendlyName();
        if (string.IsNullOrWhiteSpace(UserName))
        {
            UserName = context.User.UserName ?? "Administrator";
        }

        UserEmail = string.IsNullOrWhiteSpace(context.User.Email) ? "-" : context.User.Email!;
        UserInitials = GetInitials(UserName);
        CanManage = context.CanManage;

        await EnsureTableAsync();
        await EnsureDguidValuesAsync(context.OwnerUserId);
        Records = await LoadRecordsAsync(context.OwnerUserId);
        for (var i = 0; i < Records.Count; i++)
        {
            Records[i].DisplayId = i + 1;
        }

        RecordsJson = JsonSerializer.Serialize(Records, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Default,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        return Page();
    }

    public async Task<IActionResult> OnGetNextIdentityAsync()
    {
        try
        {
            var context = await GetAccessContextAsync();
            if (context == null)
            {
                return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
            }

            await EnsureTableAsync();
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT MAX(Id) FROM Permissions WHERE OwnerUserId = @owner";
            AddParameter(command, "@owner", context.OwnerUserId);
            var result = await command.ExecuteScalarAsync();
            var nextId = result == null || result is DBNull ? 1 : Convert.ToInt32(result) + 1;

            return new JsonResult(new
            {
                success = true,
                nextId,
                dguid = Guid.NewGuid().ToString().ToUpperInvariant()
            });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("Permissions.NextIdentity", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Unable to load next identity." }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] PermissionRecordPayload payload)
    {
        try
        {
            var context = await GetAccessContextAsync();
            if (context == null)
            {
                return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
            }

            if (!context.CanManage)
            {
                return new JsonResult(new { success = false, message = "You do not have permission to create records." }) { StatusCode = 403 };
            }

            var validation = ValidatePayload(payload);
            if (!validation.IsValid)
            {
                return new JsonResult(new { success = false, message = validation.Message }) { StatusCode = 400 };
            }

            await EnsureTableAsync();
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            var now = DateTime.UtcNow;
            var dguid = Guid.NewGuid();
            var provider = _db.Database.ProviderName ?? string.Empty;
            var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

            await using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO Permissions
(OwnerUserId, DGUID, Name, SubjectType, SubjectKey, AccessLevel, Scope, IsEnabled, Notes, CreatedAtUtc, UpdatedAtUtc)
VALUES (@owner, @dguid, @name, @subjectType, @subjectKey, @accessLevel, @scope, @enabled, @notes, @created, @updated)";
            AddParameter(command, "@owner", context.OwnerUserId);
            AddParameter(command, "@dguid", isSqlite ? dguid.ToString("N") : dguid);
            AddParameter(command, "@name", payload.Name.Trim());
            AddParameter(command, "@subjectType", payload.SubjectType.Trim());
            AddParameter(command, "@subjectKey", payload.SubjectKey.Trim());
            AddParameter(command, "@accessLevel", payload.AccessLevel.Trim());
            AddParameter(command, "@scope", payload.Scope.Trim());
            AddParameter(command, "@enabled", payload.IsEnabled);
            AddParameter(command, "@notes", payload.Notes?.Trim() ?? string.Empty);
            AddParameter(command, "@created", now);
            AddParameter(command, "@updated", now);
            var inserted = await command.ExecuteNonQueryAsync();
            if (inserted <= 0)
            {
                return new JsonResult(new { success = false, message = "Unable to create record." }) { StatusCode = 500 };
            }

            return new JsonResult(new { success = true, message = "Record created." });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("Permissions.Create", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Unable to create record." }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostUpdateAsync([FromBody] PermissionRecordPayload payload)
    {
        try
        {
            var context = await GetAccessContextAsync();
            if (context == null)
            {
                return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
            }

            if (!context.CanManage)
            {
                return new JsonResult(new { success = false, message = "You do not have permission to update records." }) { StatusCode = 403 };
            }

            if (payload.Id <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid record." }) { StatusCode = 400 };
            }

            var validation = ValidatePayload(payload);
            if (!validation.IsValid)
            {
                return new JsonResult(new { success = false, message = validation.Message }) { StatusCode = 400 };
            }

            await EnsureTableAsync();
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText = @"UPDATE Permissions
SET Name = @name,
    SubjectType = @subjectType,
    SubjectKey = @subjectKey,
    AccessLevel = @accessLevel,
    Scope = @scope,
    IsEnabled = @enabled,
    Notes = @notes,
    UpdatedAtUtc = @updated
WHERE Id = @id AND OwnerUserId = @owner";
            AddParameter(command, "@name", payload.Name.Trim());
            AddParameter(command, "@subjectType", payload.SubjectType.Trim());
            AddParameter(command, "@subjectKey", payload.SubjectKey.Trim());
            AddParameter(command, "@accessLevel", payload.AccessLevel.Trim());
            AddParameter(command, "@scope", payload.Scope.Trim());
            AddParameter(command, "@enabled", payload.IsEnabled);
            AddParameter(command, "@notes", payload.Notes?.Trim() ?? string.Empty);
            AddParameter(command, "@updated", DateTime.UtcNow);
            AddParameter(command, "@id", payload.Id);
            AddParameter(command, "@owner", context.OwnerUserId);
            var updated = await command.ExecuteNonQueryAsync();
            if (updated <= 0)
            {
                return new JsonResult(new { success = false, message = "Record not found or not updated." }) { StatusCode = 404 };
            }

            return new JsonResult(new { success = true, message = "Record updated." });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("Permissions.Update", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Unable to update record." }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync([FromBody] DeletePayload payload)
    {
        try
        {
            var context = await GetAccessContextAsync();
            if (context == null)
            {
                return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
            }

            if (!context.CanManage)
            {
                return new JsonResult(new { success = false, message = "You do not have permission to delete records." }) { StatusCode = 403 };
            }

            if (payload.Id <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid record." }) { StatusCode = 400 };
            }

            await EnsureTableAsync();
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Permissions WHERE Id = @id AND OwnerUserId = @owner";
            AddParameter(command, "@id", payload.Id);
            AddParameter(command, "@owner", context.OwnerUserId);
            await command.ExecuteNonQueryAsync();

            return new JsonResult(new { success = true, message = "Record deleted." });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("Permissions.Delete", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Unable to delete record." }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostDatabaseSyncAsync()
    {
        try
        {
            var context = await GetAccessContextAsync();
            if (context == null)
            {
                return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
            }

            await EnsureTableAsync();
            await EnsureDguidValuesAsync(context.OwnerUserId);
            return new JsonResult(new { success = true, repaired = 0, message = "Database synced." });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("Permissions.DatabaseSync", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Unable to sync database." }) { StatusCode = 500 };
        }
    }

    private async Task<List<PermissionRecordRow>> LoadRecordsAsync(string ownerUserId)
    {
        var list = new List<PermissionRecordRow>();
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        await using var command = connection.CreateCommand();
        async Task RunQueryAsync(bool scoped)
        {
            command.Parameters.Clear();
            command.CommandText = scoped
                ? @"SELECT Id, DGUID, Name, SubjectType, SubjectKey, AccessLevel, Scope, IsEnabled, Notes, CreatedAtUtc, UpdatedAtUtc
FROM Permissions
WHERE OwnerUserId = @owner
ORDER BY UpdatedAtUtc DESC"
                : @"SELECT Id, DGUID, Name, SubjectType, SubjectKey, AccessLevel, Scope, IsEnabled, Notes, CreatedAtUtc, UpdatedAtUtc
FROM Permissions
ORDER BY UpdatedAtUtc DESC";
            if (scoped)
            {
                AddParameter(command, "@owner", ownerUserId);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new PermissionRecordRow
                {
                    Id = reader.GetInt32(0),
                    Dguid = isSqlite ? reader.GetString(1) : reader.GetGuid(1).ToString(),
                    Name = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    SubjectType = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    SubjectKey = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    AccessLevel = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    Scope = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    IsEnabled = reader.IsDBNull(7) ? true : reader.GetBoolean(7),
                    Notes = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    CreatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(9)) : reader.GetDateTime(9),
                    UpdatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(10)) : reader.GetDateTime(10)
                });
            }
        }

        await RunQueryAsync(scoped: true);
        if (list.Count == 0)
        {
            await RunQueryAsync(scoped: false);
        }

        return list;
    }

    private async Task EnsureTableAsync()
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS Permissions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerUserId TEXT NOT NULL,
    DGUID TEXT NOT NULL,
    Name TEXT NOT NULL,
    SubjectType TEXT NOT NULL,
    SubjectKey TEXT NOT NULL,
    AccessLevel TEXT NOT NULL,
    Scope TEXT NOT NULL,
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    Notes TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);");
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Permissions' AND xtype='U')
CREATE TABLE Permissions (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NOT NULL,
    DGUID UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    SubjectType NVARCHAR(80) NOT NULL,
    SubjectKey NVARCHAR(220) NOT NULL,
    AccessLevel NVARCHAR(80) NOT NULL,
    Scope NVARCHAR(120) NOT NULL,
    IsEnabled BIT NOT NULL DEFAULT 1,
    Notes NVARCHAR(MAX) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL
);");
    }

    private async Task EnsureDguidValuesAsync(string ownerUserId)
    {
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        if (isSqlite)
        {
            command.CommandText = "UPDATE Permissions SET DGUID = lower(hex(randomblob(16))) WHERE OwnerUserId = @owner AND (DGUID IS NULL OR DGUID = '')";
        }
        else
        {
            command.CommandText = "UPDATE Permissions SET DGUID = NEWID() WHERE OwnerUserId = @owner AND (DGUID IS NULL OR DGUID = '00000000-0000-0000-0000-000000000000')";
        }

        AddParameter(command, "@owner", ownerUserId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<AccessContext?> GetAccessContextAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return null;
        }

        var member = await _db.TeamMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == user.Id);

        var ownerUserId = member?.OwnerUserId ?? user.Id;
        var canManage = member == null || string.Equals(member.Role, "Admin", StringComparison.OrdinalIgnoreCase);

        return new AccessContext
        {
            User = user,
            OwnerUserId = ownerUserId,
            CanManage = canManage
        };
    }

    private (bool IsValid, string Message) ValidatePayload(PermissionRecordPayload payload)
    {
        var name = payload.Name?.Trim() ?? string.Empty;
        var subjectType = payload.SubjectType?.Trim() ?? string.Empty;
        var subjectKey = payload.SubjectKey?.Trim() ?? string.Empty;
        var accessLevel = payload.AccessLevel?.Trim() ?? string.Empty;
        var scope = payload.Scope?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, "Name is required.");
        }

        if (string.IsNullOrWhiteSpace(subjectType))
        {
            return (false, "Subject Type is required.");
        }

        if (string.IsNullOrWhiteSpace(subjectKey))
        {
            return (false, "Subject Key is required.");
        }

        if (string.IsNullOrWhiteSpace(accessLevel))
        {
            return (false, "Access Level is required.");
        }

        if (string.IsNullOrWhiteSpace(scope))
        {
            return (false, "Scope is required.");
        }

        return (true, string.Empty);
    }

    private static void AddParameter(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string GetInitials(string name)
    {
        var initials = new string(name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]))
            .Take(2)
            .ToArray());
        return string.IsNullOrWhiteSpace(initials) ? "AD" : initials;
    }

    private sealed class AccessContext
    {
        public required ApplicationUser User { get; init; }
        public required string OwnerUserId { get; init; }
        public bool CanManage { get; init; }
    }

    public class PermissionRecordRow
    {
        public int Id { get; set; }
        public int DisplayId { get; set; }
        public string Dguid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SubjectType { get; set; } = string.Empty;
        public string SubjectKey { get; set; } = string.Empty;
        public string AccessLevel { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public string Notes { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    public class PermissionRecordPayload
    {
        public int Id { get; set; }
        public string Dguid { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string SubjectType { get; set; } = string.Empty;

        [Required]
        public string SubjectKey { get; set; } = string.Empty;

        [Required]
        public string AccessLevel { get; set; } = string.Empty;

        [Required]
        public string Scope { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = true;
        public string Notes { get; set; } = string.Empty;
    }

    public class DeletePayload
    {
        public int Id { get; set; }
    }
}

