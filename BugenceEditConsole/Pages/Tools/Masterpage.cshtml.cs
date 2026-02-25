using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Linq;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Tools;

[Authorize]
public class MasterpageModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public MasterpageModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public string UserName { get; private set; } = "Administrator";
    public string UserEmail { get; private set; } = "—";
    public string UserInitials { get; private set; } = "AD";
    public bool CanManage { get; private set; }
    public List<MasterpageRow> Masterpages { get; private set; } = new();
    public string MasterpagesJson { get; private set; } = "[]";
    public List<TemplateViewerOption> TemplateViewers { get; private set; } = new();
    public string TemplateViewersJson { get; private set; } = "[]";

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

        UserEmail = string.IsNullOrWhiteSpace(context.User.Email) ? "—" : context.User.Email!;
        UserInitials = GetInitials(UserName);
        CanManage = context.CanManage;

        await EnsureTableAsync();
        await EnsureDguidAsync(context.OwnerUserId);
        Masterpages = await LoadMasterpagesAsync(context.OwnerUserId);
        for (var i = 0; i < Masterpages.Count; i++)
        {
            Masterpages[i].DisplayId = i + 1;
        }
        MasterpagesJson = JsonSerializer.Serialize(Masterpages, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Default,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        await EnsureTempleteViewerTableAsync();
        TemplateViewers = await LoadTemplateViewersAsync(context.OwnerUserId);
        TemplateViewersJson = JsonSerializer.Serialize(TemplateViewers, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Default,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        return Page();
    }

    public Task<IActionResult> OnGetNextIdAsync() => OnGetNextIdentityAsync();

    public async Task<IActionResult> OnGetNextIdentityAsync()
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
        command.CommandText = "SELECT MAX(Id) FROM Masterpages WHERE OwnerUserId = @owner";
        AddParameter(command, "@owner", context.OwnerUserId);
        var result = await command.ExecuteScalarAsync();
        var nextId = result == null || result is DBNull ? 1 : Convert.ToInt32(result) + 1;
        return new JsonResult(new { success = true, nextId, dguid = Guid.NewGuid().ToString() });
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] MasterpagePayload payload)
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

        if (string.IsNullOrWhiteSpace(payload.Name) || payload.TemplateViewerId <= 0 || string.IsNullOrWhiteSpace(payload.MasterpageText))
        {
            return new JsonResult(new { success = false, message = "Please provide name, template viewer and masterpage." }) { StatusCode = 400 };
        }

        await EnsureTableAsync();
        await EnsureDguidAsync(context.OwnerUserId);
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var now = DateTime.UtcNow;
        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var dguidValue = ResolveDguid(payload.Dguid);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"INSERT INTO Masterpages (OwnerUserId, DGUID, Name, TemplateViewerId, MasterpageText, CreatedAtUtc, UpdatedAtUtc)
VALUES (@owner, @dguid, @name, @viewer, @text, @created, @updated)";
            AddParameter(command, "@owner", context.OwnerUserId);
            AddParameter(command, "@dguid", isSqlite ? dguidValue.ToString("N") : dguidValue);
            AddParameter(command, "@name", payload.Name.Trim());
            AddParameter(command, "@viewer", payload.TemplateViewerId);
            AddParameter(command, "@text", payload.MasterpageText.Trim());
            AddParameter(command, "@created", now);
            AddParameter(command, "@updated", now);
            await command.ExecuteNonQueryAsync();
        }

        return new JsonResult(new { success = true, message = "Record created." });
    }

    public async Task<IActionResult> OnPostUpdateAsync([FromBody] MasterpagePayload payload)
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

        if (payload.Id <= 0 || string.IsNullOrWhiteSpace(payload.Name) || payload.TemplateViewerId <= 0 || string.IsNullOrWhiteSpace(payload.MasterpageText))
        {
            return new JsonResult(new { success = false, message = "Please provide name, template viewer and masterpage." }) { StatusCode = 400 };
        }

        await EnsureTableAsync();
        await EnsureDguidAsync(context.OwnerUserId);
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"UPDATE Masterpages
SET Name = @name, TemplateViewerId = @viewer, MasterpageText = @text, UpdatedAtUtc = @updated
WHERE Id = @id AND OwnerUserId = @owner";
            AddParameter(command, "@name", payload.Name.Trim());
            AddParameter(command, "@viewer", payload.TemplateViewerId);
            AddParameter(command, "@text", payload.MasterpageText.Trim());
            AddParameter(command, "@updated", DateTime.UtcNow);
            AddParameter(command, "@id", payload.Id);
            AddParameter(command, "@owner", context.OwnerUserId);
            await command.ExecuteNonQueryAsync();
        }

        return new JsonResult(new { success = true, message = "Record updated." });
    }

    public async Task<IActionResult> OnPostDeleteAsync([FromBody] DeletePayload payload)
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

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM Masterpages WHERE Id = @id AND OwnerUserId = @owner";
            AddParameter(command, "@id", payload.Id);
            AddParameter(command, "@owner", context.OwnerUserId);
            await command.ExecuteNonQueryAsync();
        }

        return new JsonResult(new { success = true, message = "Record deleted." });
    }

    public async Task<IActionResult> OnPostDatabaseSyncAsync()
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        await EnsureTableAsync();
        await EnsureDguidAsync(context.OwnerUserId);
        await EnsureTempleteViewerTableAsync();

        return new JsonResult(new { success = true, repaired = 0, message = "Database synced." });
    }

    private async Task<List<MasterpageRow>> LoadMasterpagesAsync(string ownerUserId)
    {
        var list = new List<MasterpageRow>();
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
            list.Clear();
            command.Parameters.Clear();
            command.CommandText = scoped
                ? @"SELECT m.Id, m.DGUID, m.Name, m.TemplateViewerId, v.Name, m.MasterpageText, m.CreatedAtUtc, m.UpdatedAtUtc
FROM Masterpages m
LEFT JOIN TempleteViewers v ON v.Id = m.TemplateViewerId
WHERE m.OwnerUserId = @owner
ORDER BY m.CreatedAtUtc DESC"
                : @"SELECT m.Id, m.DGUID, m.Name, m.TemplateViewerId, v.Name, m.MasterpageText, m.CreatedAtUtc, m.UpdatedAtUtc
FROM Masterpages m
LEFT JOIN TempleteViewers v ON v.Id = m.TemplateViewerId
ORDER BY m.CreatedAtUtc DESC";

            if (scoped)
            {
                AddParameter(command, "@owner", ownerUserId);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new MasterpageRow
                {
                    Id = reader.GetInt32(0),
                    Dguid = isSqlite ? reader.GetString(1) : reader.GetGuid(1).ToString(),
                    Name = reader.GetString(2),
                    TemplateViewerId = reader.GetInt32(3),
                    TemplateViewerName = reader.IsDBNull(4) ? "-" : reader.GetString(4),
                    MasterpageText = reader.GetString(5),
                    CreatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(6)) : reader.GetDateTime(6),
                    UpdatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(7)) : reader.GetDateTime(7)
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

    private async Task<List<TemplateViewerOption>> LoadTemplateViewersAsync(string ownerUserId)
    {
        var list = new List<TemplateViewerOption>();
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        async Task RunQueryAsync(bool scoped)
        {
            list.Clear();
            command.Parameters.Clear();
            command.CommandText = scoped
                ? "SELECT Id, DGUID, Name, ViewerType, TemplateText, UpdatedAtUtc FROM TempleteViewers WHERE OwnerUserId = @owner ORDER BY Name"
                : "SELECT Id, DGUID, Name, ViewerType, TemplateText, UpdatedAtUtc FROM TempleteViewers ORDER BY Name";

            if (scoped)
            {
                AddParameter(command, "@owner", ownerUserId);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new TemplateViewerOption
                {
                    Id = reader.GetInt32(0),
                    Dguid = isSqlite ? reader.GetString(1) : reader.GetGuid(1).ToString(),
                    Name = reader.GetString(2),
                    ViewerType = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    TemplateText = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    UpdatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(5)) : reader.GetDateTime(5)
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
CREATE TABLE IF NOT EXISTS Masterpages (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerUserId TEXT NOT NULL,
    DGUID TEXT NOT NULL,
    Name TEXT NOT NULL,
    TemplateViewerId INTEGER NOT NULL,
    MasterpageText TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);");
            using (var connection = _db.Database.GetDbConnection())
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }
                await using var pragma = connection.CreateCommand();
                pragma.CommandText = "PRAGMA table_info(\"Masterpages\")";
                await using var reader = await pragma.ExecuteReaderAsync();
                var hasDguid = false;
                while (await reader.ReadAsync())
                {
                    var colName = reader.GetString(1);
                    if (string.Equals(colName, "DGUID", StringComparison.OrdinalIgnoreCase))
                    {
                        hasDguid = true;
                        break;
                    }
                }
                if (!hasDguid)
                {
                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE Masterpages ADD COLUMN DGUID TEXT;");
                }
            }
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Masterpages' AND xtype='U')
CREATE TABLE Masterpages (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NOT NULL,
    DGUID UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    TemplateViewerId INT NOT NULL,
    MasterpageText NVARCHAR(MAX) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL
);");
        await _db.Database.ExecuteSqlRawAsync(@"
IF EXISTS (SELECT * FROM sysobjects WHERE name='Masterpages' AND xtype='U')
AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'DGUID' AND Object_ID = Object_ID(N'Masterpages'))
ALTER TABLE Masterpages ADD DGUID UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
");
    }

    private async Task EnsureTempleteViewerTableAsync()
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS TempleteViewers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerUserId TEXT NOT NULL,
    DGUID TEXT NOT NULL,
    Name TEXT NOT NULL,
    ViewerType TEXT NOT NULL,
    TemplateText TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);");
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='TempleteViewers' AND xtype='U')
CREATE TABLE TempleteViewers (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NOT NULL,
    DGUID UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    ViewerType NVARCHAR(200) NOT NULL,
    TemplateText NVARCHAR(MAX) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL
);");
    }

    private async Task EnsureDguidAsync(string ownerUserId)
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
            command.CommandText = "UPDATE Masterpages SET DGUID = lower(hex(randomblob(16))) WHERE OwnerUserId = @owner AND (DGUID IS NULL OR DGUID = '')";
        }
        else
        {
            command.CommandText = "UPDATE Masterpages SET DGUID = NEWID() WHERE OwnerUserId = @owner AND (DGUID IS NULL OR DGUID = '00000000-0000-0000-0000-000000000000')";
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

    public class MasterpageRow
    {
        public int Id { get; set; }
        public int DisplayId { get; set; }
        public string Dguid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int TemplateViewerId { get; set; }
        public string TemplateViewerName { get; set; } = string.Empty;
        public string MasterpageText { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    public class TemplateViewerOption
    {
        public int Id { get; set; }
        public string Dguid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ViewerType { get; set; } = string.Empty;
        public string TemplateText { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; }
    }

    public class MasterpagePayload
    {
        public int Id { get; set; }
        public string? Dguid { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public int TemplateViewerId { get; set; }

        [Required]
        public string MasterpageText { get; set; } = string.Empty;
    }

    public class DeletePayload
    {
        public int Id { get; set; }
    }

    private static Guid ResolveDguid(string? value)
    {
        return Guid.TryParse(value, out var parsed) ? parsed : Guid.NewGuid();
    }
}


