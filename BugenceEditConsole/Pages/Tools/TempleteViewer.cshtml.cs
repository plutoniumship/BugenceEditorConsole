using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
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
public class TempleteViewerModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public TempleteViewerModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public string UserName { get; private set; } = "Administrator";
    public string UserEmail { get; private set; } = "â€”";
    public string UserInitials { get; private set; } = "AD";
    public bool CanManage { get; private set; }
    public List<ViewerRow> Viewers { get; private set; } = new();
    public string ViewersJson { get; private set; } = "[]";
    public string ApplicationsJson { get; private set; } = "[]";

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

        UserEmail = string.IsNullOrWhiteSpace(context.User.Email) ? "â€”" : context.User.Email!;
        UserInitials = GetInitials(UserName);
        CanManage = context.CanManage;

        await EnsureTableAsync();
        await EnsureViewerDguidAsync(context.OwnerUserId, context.CompanyId);
        Viewers = await LoadViewersAsync(context.OwnerUserId, context.CompanyId);
        for (var i = 0; i < Viewers.Count; i++)
        {
            Viewers[i].DisplayId = i + 1;
        }
        ViewersJson = JsonSerializer.Serialize(Viewers, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Default,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        var appTables = await LoadApplicationTablesAsync(context.OwnerUserId);
        ApplicationsJson = JsonSerializer.Serialize(appTables, new JsonSerializerOptions
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
        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        command.CommandText = "SELECT MAX(Id) FROM TempleteViewers WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
        AddParameter(command, "@owner", context.OwnerUserId);
        AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
        var result = await command.ExecuteScalarAsync();
        var nextId = result == null || result is DBNull ? 1 : Convert.ToInt32(result) + 1;
        return new JsonResult(new { success = true, nextId, dguid = Guid.NewGuid().ToString() });
    }

    public async Task<IActionResult> OnGetAppTablesAsync()
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        var tables = await LoadApplicationTablesAsync(context.OwnerUserId);
        return new JsonResult(new { success = true, tables });
    }

    public async Task<IActionResult> OnGetAppQueriesAsync()
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        var queries = await LoadQuerySelectorsAsync(context.OwnerUserId, context.CompanyId);
        return new JsonResult(new { success = true, queries });
    }

    public async Task<IActionResult> OnGetSubTemplatesAsync()
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        var subTemplates = await LoadSubTemplatesAsync(context.OwnerUserId, context.CompanyId);
        return new JsonResult(new { success = true, subTemplates });
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] ViewerPayload payload)
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

        if (string.IsNullOrWhiteSpace(payload.Name) || string.IsNullOrWhiteSpace(payload.ViewerType) || string.IsNullOrWhiteSpace(payload.TemplateText))
        {
            return new JsonResult(new { success = false, message = "Please provide name, viewer type and templete viewer." }) { StatusCode = 400 };
        }

        await EnsureTableAsync();
        await EnsureViewerDguidAsync(context.OwnerUserId, context.CompanyId);
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var now = DateTime.UtcNow;
        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var dguidValue = Guid.NewGuid();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"INSERT INTO TempleteViewers (OwnerUserId, CompanyId, DGUID, Name, ViewerType, TemplateText, CreatedAtUtc, UpdatedAtUtc)
VALUES (@owner, @company, @dguid, @name, @type, @text, @created, @updated)";
            AddParameter(command, "@owner", context.OwnerUserId);
            AddParameter(command, "@company", context.CompanyId.HasValue ? (isSqlite ? context.CompanyId.Value.ToString() : context.CompanyId.Value) : null);
            AddParameter(command, "@dguid", isSqlite ? dguidValue.ToString("N") : dguidValue);
            AddParameter(command, "@name", payload.Name.Trim());
            AddParameter(command, "@type", payload.ViewerType.Trim());
            AddParameter(command, "@text", payload.TemplateText.Trim());
            AddParameter(command, "@created", now);
            AddParameter(command, "@updated", now);
            var inserted = await command.ExecuteNonQueryAsync();
            if (inserted <= 0)
            {
                return new JsonResult(new { success = false, message = "Unable to create record." }) { StatusCode = 500 };
            }
        }

        return new JsonResult(new { success = true, message = "Record created." });
    }

    public async Task<IActionResult> OnPostUpdateAsync([FromBody] ViewerPayload payload)
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

        if (payload.Id <= 0 || string.IsNullOrWhiteSpace(payload.Name) || string.IsNullOrWhiteSpace(payload.ViewerType) || string.IsNullOrWhiteSpace(payload.TemplateText))
        {
            return new JsonResult(new { success = false, message = "Please provide name, viewer type and templete viewer." }) { StatusCode = 400 };
        }

        await EnsureTableAsync();
        await EnsureViewerDguidAsync(context.OwnerUserId, context.CompanyId);
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"UPDATE TempleteViewers
	SET Name = @name, ViewerType = @type, TemplateText = @text, UpdatedAtUtc = @updated
	WHERE Id = @id AND OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
            var provider = _db.Database.ProviderName ?? string.Empty;
            var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
            AddParameter(command, "@name", payload.Name.Trim());
            AddParameter(command, "@type", payload.ViewerType.Trim());
            AddParameter(command, "@text", payload.TemplateText.Trim());
            AddParameter(command, "@updated", DateTime.UtcNow);
            AddParameter(command, "@id", payload.Id);
            AddParameter(command, "@owner", context.OwnerUserId);
            AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
            var updated = await command.ExecuteNonQueryAsync();
            if (updated <= 0)
            {
                return new JsonResult(new { success = false, message = "Record not found or not updated." }) { StatusCode = 404 };
            }
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
            var provider = _db.Database.ProviderName ?? string.Empty;
            var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
            command.CommandText = "DELETE FROM TempleteViewers WHERE Id = @id AND OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
            AddParameter(command, "@id", payload.Id);
            AddParameter(command, "@owner", context.OwnerUserId);
            AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
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
        await EnsureViewerDguidAsync(context.OwnerUserId, context.CompanyId);
        await EnsureQuerySelectorTableAsync();
        await EnsureQuerySelectorDguidAsync(context.OwnerUserId, context.CompanyId);

        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var repaired = 0;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE TempleteViewers SET CompanyId=@company, UpdatedAtUtc=@updated WHERE OwnerUserId=@owner AND CompanyId IS NULL";
            AddParameter(command, "@owner", context.OwnerUserId);
            AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
            AddParameter(command, "@updated", DateTime.UtcNow);
            repaired += await command.ExecuteNonQueryAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE DatabaseQuerySelectors SET CompanyId=@company, UpdatedAtUtc=@updated WHERE OwnerUserId=@owner AND CompanyId IS NULL";
            AddParameter(command, "@owner", context.OwnerUserId);
            AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
            AddParameter(command, "@updated", DateTime.UtcNow);
            repaired += await command.ExecuteNonQueryAsync();
        }

        return new JsonResult(new { success = true, repaired, message = $"Database synced. {repaired} record(s) repaired." });
    }

    private async Task<List<ViewerRow>> LoadViewersAsync(string ownerUserId, Guid? companyId)
    {
        var list = new List<ViewerRow>();
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
                ? "SELECT Id, CompanyId, DGUID, Name, ViewerType, TemplateText, CreatedAtUtc, UpdatedAtUtc FROM TempleteViewers WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) ORDER BY CreatedAtUtc DESC"
                : "SELECT Id, CompanyId, DGUID, Name, ViewerType, TemplateText, CreatedAtUtc, UpdatedAtUtc FROM TempleteViewers ORDER BY CreatedAtUtc DESC";
            if (scoped)
            {
                AddParameter(command, "@owner", ownerUserId);
                AddCompanyParameter(command, "@company", companyId, isSqlite);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ViewerRow
                {
                    Id = reader.GetInt32(0),
                    CompanyId = reader.IsDBNull(1) ? string.Empty : (isSqlite ? reader.GetString(1) : reader.GetGuid(1).ToString()),
                    Dguid = isSqlite ? reader.GetString(2) : reader.GetGuid(2).ToString(),
                    Name = reader.GetString(3),
                    ViewerType = reader.GetString(4),
                    TemplateText = reader.GetString(5),
                    CreatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(6)) : reader.GetDateTime(6),
                    UpdatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(7)) : reader.GetDateTime(7)
                });
            }
        }

        var seenIds = new HashSet<int>();
        await RunQueryAsync(scoped: true);
        foreach (var row in list)
        {
            seenIds.Add(row.Id);
        }

        await RunQueryAsync(scoped: false);
        if (seenIds.Count > 0)
        {
            list = list
                .GroupBy(x => x.Id)
                .Select(g => g.First())
                .OrderByDescending(x => x.CreatedAtUtc)
                .ToList();
        }

        return list;
    }

    private async Task EnsureTableAsync()
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS TempleteViewers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerUserId TEXT NOT NULL,
    CompanyId TEXT NULL,
    DGUID TEXT NOT NULL,
    Name TEXT NOT NULL,
    ViewerType TEXT NOT NULL,
    TemplateText TEXT NOT NULL,
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
                pragma.CommandText = "PRAGMA table_info(\"TempleteViewers\")";
                await using var reader = await pragma.ExecuteReaderAsync();
                var hasName = false;
                var hasDguid = false;
                while (await reader.ReadAsync())
                {
                    var colName = reader.GetString(1);
                    if (string.Equals(colName, "Name", StringComparison.OrdinalIgnoreCase))
                    {
                        hasName = true;
                    }
                    if (string.Equals(colName, "DGUID", StringComparison.OrdinalIgnoreCase))
                    {
                        hasDguid = true;
                    }
                }
                if (!hasName)
                {
                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE TempleteViewers ADD COLUMN Name TEXT NOT NULL DEFAULT '';");
                }
                if (!hasDguid)
                {
                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE TempleteViewers ADD COLUMN DGUID TEXT;");
                }
                await using var companyPragma = connection.CreateCommand();
                companyPragma.CommandText = "PRAGMA table_info(\"TempleteViewers\")";
                await using var companyReader = await companyPragma.ExecuteReaderAsync();
                var hasCompanyId = false;
                while (await companyReader.ReadAsync())
                {
                    var colName = companyReader.GetString(1);
                    if (string.Equals(colName, "CompanyId", StringComparison.OrdinalIgnoreCase))
                    {
                        hasCompanyId = true;
                        break;
                    }
                }
                if (!hasCompanyId)
                {
                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE TempleteViewers ADD COLUMN CompanyId TEXT NULL;");
                }
            }
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='TempleteViewers' AND xtype='U')
CREATE TABLE TempleteViewers (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NOT NULL,
    CompanyId UNIQUEIDENTIFIER NULL,
    DGUID UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    ViewerType NVARCHAR(200) NOT NULL,
    TemplateText NVARCHAR(MAX) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL
);");
        await _db.Database.ExecuteSqlRawAsync(@"
IF EXISTS (SELECT * FROM sysobjects WHERE name='TempleteViewers' AND xtype='U')
AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'Name' AND Object_ID = Object_ID(N'TempleteViewers'))
ALTER TABLE TempleteViewers ADD Name NVARCHAR(200) NOT NULL DEFAULT('');
");
        await _db.Database.ExecuteSqlRawAsync(@"
IF EXISTS (SELECT * FROM sysobjects WHERE name='TempleteViewers' AND xtype='U')
AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'DGUID' AND Object_ID = Object_ID(N'TempleteViewers'))
ALTER TABLE TempleteViewers ADD DGUID UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
");
        await _db.Database.ExecuteSqlRawAsync(@"
IF EXISTS (SELECT * FROM sysobjects WHERE name='TempleteViewers' AND xtype='U')
AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'CompanyId' AND Object_ID = Object_ID(N'TempleteViewers'))
ALTER TABLE TempleteViewers ADD CompanyId UNIQUEIDENTIFIER NULL;
");
    }

    private async Task EnsureViewerDguidAsync(string ownerUserId, Guid? companyId)
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
            command.CommandText = "UPDATE TempleteViewers SET DGUID = lower(hex(randomblob(16))) WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) AND (DGUID IS NULL OR DGUID = '')";
        }
        else
        {
            command.CommandText = "UPDATE TempleteViewers SET DGUID = NEWID() WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) AND (DGUID IS NULL OR DGUID = '00000000-0000-0000-0000-000000000000')";
        }
        AddParameter(command, "@owner", ownerUserId);
        AddCompanyParameter(command, "@company", companyId, isSqlite);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<SubTemplateInfo>> LoadSubTemplatesAsync(string ownerUserId, Guid? companyId)
    {
        var list = new List<SubTemplateInfo>();
        await EnsureTableAsync();
        await EnsureViewerDguidAsync(ownerUserId, companyId);
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
                ? "SELECT Id, Name, DGUID, TemplateText FROM TempleteViewers WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) AND ViewerType = @type ORDER BY UpdatedAtUtc DESC"
                : "SELECT Id, Name, DGUID, TemplateText FROM TempleteViewers WHERE ViewerType = @type ORDER BY UpdatedAtUtc DESC";
            if (scoped)
            {
                AddParameter(command, "@owner", ownerUserId);
                AddCompanyParameter(command, "@company", companyId, isSqlite);
            }
            AddParameter(command, "@type", "Sub Templete");

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var dguid = isSqlite ? reader.GetString(2) : reader.GetGuid(2).ToString();
                var token = dguid.Replace("-", string.Empty);
                list.Add(new SubTemplateInfo
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    DguidToken = token.Length >= 16 ? token[..16] : token,
                    TemplateText = reader.GetString(3)
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

    private async Task<List<ApplicationTableInfo>> LoadApplicationTablesAsync(string ownerUserId)
    {
        var tables = new Dictionary<Guid, ApplicationTableInfo>();
        await EnsureMetadataTablesAsync();
        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, TableName FROM ApplicationTables WHERE OwnerUserId = @owner ORDER BY TableName";
            AddParameter(command, "@owner", ownerUserId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = isSqlite ? Guid.Parse(reader.GetString(0)) : reader.GetGuid(0);
                var token = id.ToString("N");
                tables[id] = new ApplicationTableInfo
                {
                    Id = id,
                    TableName = reader.GetString(1),
                    DguidToken = token.Length >= 16 ? token[..16] : token
                };
            }
        }

        if (tables.Count == 0)
        {
            await using var fallbackCommand = connection.CreateCommand();
            fallbackCommand.CommandText = "SELECT Id, TableName FROM ApplicationTables ORDER BY TableName";
            await using var fallbackReader = await fallbackCommand.ExecuteReaderAsync();
            while (await fallbackReader.ReadAsync())
            {
                var id = isSqlite ? Guid.Parse(fallbackReader.GetString(0)) : fallbackReader.GetGuid(0);
                var token = id.ToString("N");
                tables[id] = new ApplicationTableInfo
                {
                    Id = id,
                    TableName = fallbackReader.GetString(1),
                    DguidToken = token.Length >= 16 ? token[..16] : token
                };
            }
        }

        if (tables.Count == 0)
        {
            return new List<ApplicationTableInfo>();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ApplicationTableId, ColumnName FROM ApplicationTableColumns WHERE ApplicationTableId IN (" +
                                  string.Join(", ", tables.Keys.Select(k => $"'{k}'")) +
                                  ") ORDER BY ColumnName";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = isSqlite ? Guid.Parse(reader.GetString(0)) : reader.GetGuid(0);
                if (!tables.TryGetValue(id, out var table))
                {
                    continue;
                }
                var name = reader.GetString(1);
                if (string.Equals(name, "DGUID", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                table.Columns.Add(name);
            }
        }

        foreach (var table in tables.Values)
        {
            var schemaColumns = await LoadSchemaColumnsAsync(connection, table.TableName, isSqlite);
            if (schemaColumns.Count == 0)
            {
                continue;
            }
            var existing = new HashSet<string>(table.Columns, StringComparer.OrdinalIgnoreCase);
            foreach (var column in schemaColumns)
            {
                if (existing.Add(column))
                {
                    table.Columns.Add(column);
                }
            }
        }

        return tables.Values.ToList();
    }

    private static async Task<List<string>> LoadSchemaColumnsAsync(DbConnection connection, string tableName, bool isSqlite)
    {
        var columns = new List<string>();
        if (isSqlite)
        {
            await using var pragma = connection.CreateCommand();
            pragma.CommandText = $"PRAGMA table_info(\"{tableName}\")";
            await using var reader = await pragma.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                if (string.Equals(name, "DGUID", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                columns.Add(name);
            }
            return columns;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = @name";
        AddParameter(command, "@name", tableName);
        await using var sqlReader = await command.ExecuteReaderAsync();
        while (await sqlReader.ReadAsync())
        {
            var name = sqlReader.GetString(0);
            if (string.Equals(name, "DGUID", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            columns.Add(name);
        }
        return columns;
    }

    private async Task EnsureMetadataTablesAsync()
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ApplicationTables (
    Id TEXT PRIMARY KEY,
    OwnerUserId TEXT NOT NULL,
    TableName TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ApplicationTableColumns (
    Id TEXT PRIMARY KEY,
    ApplicationTableId TEXT NOT NULL,
    ColumnName TEXT NOT NULL,
    DataType TEXT NOT NULL,
    Length INTEGER NULL,
    Precision INTEGER NULL,
    Scale INTEGER NULL,
    IsNullable INTEGER NOT NULL,
    CreatedAtUtc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ApplicationRecords (
    RecordId INTEGER PRIMARY KEY AUTOINCREMENT,
    ApplicationTableId TEXT NOT NULL,
    DGUID TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL
);");
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ApplicationTables' AND xtype='U')
CREATE TABLE ApplicationTables (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NOT NULL,
    TableName NVARCHAR(128) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL
);
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ApplicationTableColumns' AND xtype='U')
CREATE TABLE ApplicationTableColumns (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    ApplicationTableId UNIQUEIDENTIFIER NOT NULL,
    ColumnName NVARCHAR(128) NOT NULL,
    DataType NVARCHAR(64) NOT NULL,
    Length INT NULL,
    Precision INT NULL,
    Scale INT NULL,
    IsNullable BIT NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL
);
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ApplicationRecords' AND xtype='U')
CREATE TABLE ApplicationRecords (
    RecordId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ApplicationTableId UNIQUEIDENTIFIER NOT NULL,
    DGUID UNIQUEIDENTIFIER NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL
);");
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
            CompanyId = user.CompanyId,
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

    private static void AddCompanyParameter(IDbCommand command, string name, Guid? companyId, bool isSqlite)
    {
        AddParameter(command, name, companyId.HasValue ? (isSqlite ? companyId.Value.ToString() : companyId.Value) : null);
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
        public Guid? CompanyId { get; init; }
        public bool CanManage { get; init; }
    }

    public class ViewerRow
    {
        public int Id { get; set; }
        public int DisplayId { get; set; }
        public string CompanyId { get; set; } = string.Empty;
        public string Dguid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ViewerType { get; set; } = string.Empty;
        public string TemplateText { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    public class ViewerPayload
    {
        public int Id { get; set; }
        public string? Dguid { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string ViewerType { get; set; } = string.Empty;

        [Required]
        public string TemplateText { get; set; } = string.Empty;
    }

    public class DeletePayload
    {
        public int Id { get; set; }
    }

    private static Guid ResolveDguid(string? value)
    {
        return Guid.TryParse(value, out var parsed) ? parsed : Guid.NewGuid();
    }

    public class ApplicationTableInfo
    {
        public Guid Id { get; set; }
        public string TableName { get; set; } = string.Empty;
        public string DguidToken { get; set; } = string.Empty;
        public List<string> Columns { get; set; } = new();
    }

    private async Task<List<QuerySelectorInfo>> LoadQuerySelectorsAsync(string ownerUserId, Guid? companyId)
    {
        var list = new List<QuerySelectorInfo>();
        await EnsureQuerySelectorTableAsync();
        await EnsureQuerySelectorDguidAsync(ownerUserId, companyId);
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        var rawQueries = new List<(int Id, string Token, string Name, string SqlText)>();
        async Task RunQueryAsync(bool scoped)
        {
            command.Parameters.Clear();
            command.CommandText = scoped
                ? "SELECT Id, DGUID, QueryName, SqlText FROM DatabaseQuerySelectors WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) ORDER BY CreatedAtUtc DESC"
                : "SELECT Id, DGUID, QueryName, SqlText FROM DatabaseQuerySelectors ORDER BY CreatedAtUtc DESC";
            if (scoped)
            {
                AddParameter(command, "@owner", ownerUserId);
                AddCompanyParameter(command, "@company", companyId, isSqlite);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var dguid = isSqlite ? reader.GetString(1) : reader.GetGuid(1).ToString();
                rawQueries.Add((
                    reader.GetInt32(0),
                    dguid.Replace("-", string.Empty),
                    reader.GetString(2),
                    reader.GetString(3)));
            }
        }

        await RunQueryAsync(scoped: true);
        if (rawQueries.Count == 0)
        {
            await RunQueryAsync(scoped: false);
        }

        foreach (var query in rawQueries)
        {
            var sql = query.SqlText;
            var columns = ParseSelectedColumns(sql);
            if (ContainsWildcardProjection(sql))
            {
                var resolvedColumns = await TryResolveColumnsFromSelectAsync(connection, sql);
                if (resolvedColumns.Count > 0)
                {
                    columns = resolvedColumns;
                }
            }

            list.Add(new QuerySelectorInfo
            {
                Id = query.Id,
                Name = query.Name,
                SqlText = sql,
                DguidToken = query.Token.Length >= 16 ? query.Token[..16] : query.Token,
                Columns = columns
            });
        }

        return list;
    }

    private async Task EnsureQuerySelectorTableAsync()
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS DatabaseQuerySelectors (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerUserId TEXT NOT NULL,
    CompanyId TEXT NULL,
    DGUID TEXT NOT NULL,
    QueryName TEXT NOT NULL,
    SqlText TEXT NOT NULL,
    FieldGenerationSql TEXT NULL,
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
                pragma.CommandText = "PRAGMA table_info(\"DatabaseQuerySelectors\")";
                await using var reader = await pragma.ExecuteReaderAsync();
                var hasDguid = false;
                var hasQueryName = false;
                var hasName = false;
                var hasFieldGenerationSql = false;
                var hasCompanyId = false;
                while (await reader.ReadAsync())
                {
                    var colName = reader.GetString(1);
                    if (string.Equals(colName, "DGUID", StringComparison.OrdinalIgnoreCase))
                    {
                        hasDguid = true;
                    }
                    if (string.Equals(colName, "QueryName", StringComparison.OrdinalIgnoreCase))
                    {
                        hasQueryName = true;
                    }
                    if (string.Equals(colName, "Name", StringComparison.OrdinalIgnoreCase))
                    {
                        hasName = true;
                    }
                    if (string.Equals(colName, "FieldGenerationSql", StringComparison.OrdinalIgnoreCase))
                    {
                        hasFieldGenerationSql = true;
                    }
                    if (string.Equals(colName, "CompanyId", StringComparison.OrdinalIgnoreCase))
                    {
                        hasCompanyId = true;
                    }
                }
                if (!hasQueryName)
                {
                    if (hasName)
                    {
                        try
                        {
                            await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE DatabaseQuerySelectors RENAME COLUMN Name TO QueryName;");
                        }
                        catch
                        {
                            await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE DatabaseQuerySelectors ADD COLUMN QueryName TEXT;");
                            await _db.Database.ExecuteSqlRawAsync(@"UPDATE DatabaseQuerySelectors SET QueryName = Name WHERE (QueryName IS NULL OR QueryName = '') AND Name IS NOT NULL;");
                        }
                    }
                    else
                    {
                        await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE DatabaseQuerySelectors ADD COLUMN QueryName TEXT;");
                    }
                }
                if (!hasFieldGenerationSql)
                {
                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE DatabaseQuerySelectors ADD COLUMN FieldGenerationSql TEXT;");
                }
                if (!hasCompanyId)
                {
                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE DatabaseQuerySelectors ADD COLUMN CompanyId TEXT NULL;");
                }
                if (!hasDguid)
                {
                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE DatabaseQuerySelectors ADD COLUMN DGUID TEXT;");
                }
                if (hasName)
                {
                    await _db.Database.ExecuteSqlRawAsync(@"UPDATE DatabaseQuerySelectors SET QueryName = Name WHERE (QueryName IS NULL OR QueryName = '') AND Name IS NOT NULL;");
                }
            }
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='DatabaseQuerySelectors' AND xtype='U')
CREATE TABLE DatabaseQuerySelectors (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NOT NULL,
    CompanyId UNIQUEIDENTIFIER NULL,
    DGUID UNIQUEIDENTIFIER NOT NULL,
    QueryName NVARCHAR(200) NOT NULL,
    SqlText NVARCHAR(MAX) NOT NULL,
    FieldGenerationSql NVARCHAR(MAX) NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL
);");
        await _db.Database.ExecuteSqlRawAsync(@"
IF EXISTS (SELECT * FROM sysobjects WHERE name='DatabaseQuerySelectors' AND xtype='U')
AND EXISTS (SELECT * FROM sys.columns WHERE Name = N'Name' AND Object_ID = Object_ID(N'DatabaseQuerySelectors'))
AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'QueryName' AND Object_ID = Object_ID(N'DatabaseQuerySelectors'))
EXEC sp_rename 'DatabaseQuerySelectors.Name', 'QueryName', 'COLUMN';
");
        await _db.Database.ExecuteSqlRawAsync(@"
IF EXISTS (SELECT * FROM sysobjects WHERE name='DatabaseQuerySelectors' AND xtype='U')
AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'QueryName' AND Object_ID = Object_ID(N'DatabaseQuerySelectors'))
ALTER TABLE DatabaseQuerySelectors ADD QueryName NVARCHAR(200) NOT NULL CONSTRAINT DF_DatabaseQuerySelectors_QueryName DEFAULT('');
");
        await _db.Database.ExecuteSqlRawAsync(@"
IF EXISTS (SELECT * FROM sysobjects WHERE name='DatabaseQuerySelectors' AND xtype='U')
AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'DGUID' AND Object_ID = Object_ID(N'DatabaseQuerySelectors'))
ALTER TABLE DatabaseQuerySelectors ADD DGUID UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
");
        await _db.Database.ExecuteSqlRawAsync(@"
IF EXISTS (SELECT * FROM sysobjects WHERE name='DatabaseQuerySelectors' AND xtype='U')
AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'FieldGenerationSql' AND Object_ID = Object_ID(N'DatabaseQuerySelectors'))
ALTER TABLE DatabaseQuerySelectors ADD FieldGenerationSql NVARCHAR(MAX) NULL;
");
        await _db.Database.ExecuteSqlRawAsync(@"
IF EXISTS (SELECT * FROM sysobjects WHERE name='DatabaseQuerySelectors' AND xtype='U')
AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'CompanyId' AND Object_ID = Object_ID(N'DatabaseQuerySelectors'))
ALTER TABLE DatabaseQuerySelectors ADD CompanyId UNIQUEIDENTIFIER NULL;
");
        await _db.Database.ExecuteSqlRawAsync(@"
IF EXISTS (SELECT * FROM sysobjects WHERE name='DatabaseQuerySelectors' AND xtype='U')
AND EXISTS (SELECT * FROM sys.columns WHERE Name = N'Name' AND Object_ID = Object_ID(N'DatabaseQuerySelectors'))
AND EXISTS (SELECT * FROM sys.columns WHERE Name = N'QueryName' AND Object_ID = Object_ID(N'DatabaseQuerySelectors'))
UPDATE DatabaseQuerySelectors SET QueryName = Name WHERE (QueryName IS NULL OR LTRIM(RTRIM(QueryName)) = '') AND Name IS NOT NULL;
");
    }

    private async Task EnsureQuerySelectorDguidAsync(string ownerUserId, Guid? companyId)
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
            command.CommandText = "UPDATE DatabaseQuerySelectors SET DGUID = lower(hex(randomblob(16))) WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) AND (DGUID IS NULL OR DGUID = '')";
        }
        else
        {
            command.CommandText = "UPDATE DatabaseQuerySelectors SET DGUID = NEWID() WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) AND (DGUID IS NULL OR DGUID = '00000000-0000-0000-0000-000000000000')";
        }
        AddParameter(command, "@owner", ownerUserId);
        AddCompanyParameter(command, "@company", companyId, isSqlite);
        await command.ExecuteNonQueryAsync();
    }

    private static List<string> ParseSelectedColumns(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return new List<string>();
        }

        if (!TryExtractTopLevelSelectProjection(sql, out var projection))
        {
            return new List<string>();
        }

        var parts = SplitTopLevelComma(projection);
        if (parts.Count == 0)
        {
            return new List<string> { "*" };
        }

        var columns = new List<string>();
        foreach (var part in parts)
        {
            var cleaned = part.Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                continue;
            }

            if (cleaned == "*" || cleaned.EndsWith(".*", StringComparison.Ordinal))
            {
                return new List<string> { "*" };
            }

            var aliasMatch = Regex.Match(cleaned, "(?is)\\s+as\\s+(?<alias>(\\[[^\\]]+\\]|`[^`]+`|\"[^\"]+\"|[A-Za-z_][A-Za-z0-9_]*))\\s*$");
            var name = aliasMatch.Success ? aliasMatch.Groups["alias"].Value : cleaned;

            if (!aliasMatch.Success)
            {
                var trailingAlias = Regex.Match(cleaned, "(?is)\\s+(?<alias>(\\[[^\\]]+\\]|`[^`]+`|\"[^\"]+\"|[A-Za-z_][A-Za-z0-9_]*))\\s*$");
                if (trailingAlias.Success && !cleaned.EndsWith(")", StringComparison.Ordinal))
                {
                    name = trailingAlias.Groups["alias"].Value;
                }
                else if (cleaned.Contains('.'))
                {
                    name = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries).Last();
                }
            }

            name = Regex.Replace(name, @"[\[\]`""]", string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(name) && name != "*")
            {
                columns.Add(name);
            }
        }

        return columns;
    }

    private async Task<List<string>> TryResolveColumnsFromSelectAsync(DbConnection connection, string sql)
    {
        if (!TryExtractTopLevelSelectProjection(sql, out _))
        {
            return new List<string>();
        }

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 30;
            await using var schemaReader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.SingleResult);
            var columns = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < schemaReader.FieldCount; i++)
            {
                var name = schemaReader.GetName(i)?.Trim();
                if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "DGUID", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (seen.Add(name))
                {
                    columns.Add(name);
                }
            }

            return columns;
        }
        catch
        {
            return new List<string>();
        }
    }

    private static bool ContainsWildcardProjection(string sql)
    {
        if (!TryExtractTopLevelSelectProjection(sql, out var projection))
        {
            return false;
        }

        var parts = SplitTopLevelComma(projection);
        foreach (var part in parts)
        {
            var cleaned = part.Trim();
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                continue;
            }

            if (cleaned == "*" || cleaned.EndsWith(".*", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractTopLevelSelectProjection(string sql, out string projection)
    {
        projection = string.Empty;
        var selectIndex = IndexOfTopLevelKeyword(sql, "select", 0);
        if (selectIndex < 0)
        {
            return false;
        }

        var fromIndex = IndexOfTopLevelKeyword(sql, "from", selectIndex + 6);
        if (fromIndex <= selectIndex)
        {
            return false;
        }

        projection = sql.Substring(selectIndex + 6, fromIndex - (selectIndex + 6)).Trim();
        return projection.Length > 0;
    }

    private static List<string> SplitTopLevelComma(string text)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return list;
        }

        var start = 0;
        var depth = 0;
        var inSingle = false;
        var inDouble = false;
        var inBracket = false;
        var inBacktick = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (!inSingle && !inDouble && !inBracket && !inBacktick)
            {
                if (ch == '-' && next == '-')
                {
                    i += 2;
                    while (i < text.Length && text[i] != '\n')
                    {
                        i++;
                    }
                    continue;
                }

                if (ch == '/' && next == '*')
                {
                    i += 2;
                    while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                    {
                        i++;
                    }
                    i++;
                    continue;
                }
            }

            if (!inDouble && !inBracket && !inBacktick && ch == '\'')
            {
                inSingle = !inSingle;
                continue;
            }
            if (!inSingle && !inBracket && !inBacktick && ch == '"')
            {
                inDouble = !inDouble;
                continue;
            }
            if (!inSingle && !inDouble && !inBacktick && ch == '[')
            {
                inBracket = true;
                continue;
            }
            if (inBracket && ch == ']')
            {
                inBracket = false;
                continue;
            }
            if (!inSingle && !inDouble && !inBracket && ch == '`')
            {
                inBacktick = !inBacktick;
                continue;
            }

            if (inSingle || inDouble || inBracket || inBacktick)
            {
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }
            if (ch == ')' && depth > 0)
            {
                depth--;
                continue;
            }
            if (ch == ',' && depth == 0)
            {
                list.Add(text.Substring(start, i - start));
                start = i + 1;
            }
        }

        if (start <= text.Length)
        {
            list.Add(text[start..]);
        }

        return list;
    }

    private static int IndexOfTopLevelKeyword(string sql, string keyword, int startIndex)
    {
        var depth = 0;
        var inSingle = false;
        var inDouble = false;
        var inBracket = false;
        var inBacktick = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = Math.Max(0, startIndex); i < sql.Length; i++)
        {
            var ch = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                if (ch == '\n')
                {
                    inLineComment = false;
                }
                continue;
            }

            if (inBlockComment)
            {
                if (ch == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (!inSingle && !inDouble && !inBracket && !inBacktick)
            {
                if (ch == '-' && next == '-')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }
                if (ch == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
            }

            if (!inDouble && !inBracket && !inBacktick && ch == '\'')
            {
                inSingle = !inSingle;
                continue;
            }
            if (!inSingle && !inBracket && !inBacktick && ch == '"')
            {
                inDouble = !inDouble;
                continue;
            }
            if (!inSingle && !inDouble && !inBacktick && ch == '[')
            {
                inBracket = true;
                continue;
            }
            if (inBracket && ch == ']')
            {
                inBracket = false;
                continue;
            }
            if (!inSingle && !inDouble && !inBracket && ch == '`')
            {
                inBacktick = !inBacktick;
                continue;
            }

            if (inSingle || inDouble || inBracket || inBacktick)
            {
                continue;
            }

            if (ch == '(')
            {
                depth++;
                continue;
            }
            if (ch == ')' && depth > 0)
            {
                depth--;
                continue;
            }

            if (depth == 0 && StartsWithKeyword(sql, i, keyword))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool StartsWithKeyword(string value, int startIndex, string keyword)
    {
        if (startIndex < 0 || startIndex + keyword.Length > value.Length)
        {
            return false;
        }

        if (!value.AsSpan(startIndex, keyword.Length).Equals(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var beforeOk = startIndex == 0 || !IsSqlIdentifierChar(value[startIndex - 1]);
        var afterIndex = startIndex + keyword.Length;
        var afterOk = afterIndex >= value.Length || !IsSqlIdentifierChar(value[afterIndex]);
        return beforeOk && afterOk;
    }

    private static bool IsSqlIdentifierChar(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch == '_' || ch == '$';
    }

    public class QuerySelectorInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string SqlText { get; set; } = string.Empty;
        public string DguidToken { get; set; } = string.Empty;
        public List<string> Columns { get; set; } = new();
    }

    public class SubTemplateInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DguidToken { get; set; } = string.Empty;
        public string TemplateText { get; set; } = string.Empty;
    }
}


