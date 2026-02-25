using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
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
public class DatabaseQuerySelectorModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DebugPanelLogService _debugLogService;

    public DatabaseQuerySelectorModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, DebugPanelLogService debugLogService)
    {
        _db = db;
        _userManager = userManager;
        _debugLogService = debugLogService;
    }

    public string UserName { get; private set; } = "Administrator";
    public string UserEmail { get; private set; } = "â€”";
    public string UserInitials { get; private set; } = "AD";
    public bool CanManage { get; private set; }
    public List<QueryRow> Queries { get; private set; } = new();
    public string QueriesJson { get; private set; } = "[]";

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
        await EnsureDguidValuesAsync(context.OwnerUserId, context.CompanyId);
        Queries = await LoadQueriesAsync(context.OwnerUserId, context.CompanyId);
        QueriesJson = JsonSerializer.Serialize(Queries, new JsonSerializerOptions
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
            var provider = _db.Database.ProviderName ?? string.Empty;
            var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
            command.CommandText = "SELECT MAX(Id) FROM DatabaseQuerySelectors WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
            AddParameter(command, "@owner", context.OwnerUserId);
            AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
            var result = await command.ExecuteScalarAsync();
            var nextId = result == null || result is DBNull ? 1 : Convert.ToInt32(result) + 1;
            return new JsonResult(new { success = true, nextId, dguid = Guid.NewGuid().ToString() });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("DatabaseQuerySelector.NextId", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Unable to load next ID." }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] QueryPayload payload)
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

            if (string.IsNullOrWhiteSpace(payload.Name) || string.IsNullOrWhiteSpace(payload.SqlText))
            {
                return new JsonResult(new { success = false, message = "Please provide a name and SQL." }) { StatusCode = 400 };
            }

            await EnsureTableAsync();
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            var now = DateTime.UtcNow;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = @"INSERT INTO DatabaseQuerySelectors (OwnerUserId, CompanyId, DGUID, QueryName, SqlText, FieldGenerationSql, CreatedAtUtc, UpdatedAtUtc)
VALUES (@owner, @company, @dguid, @name, @sql, @fieldSql, @created, @updated)";
                AddParameter(command, "@owner", context.OwnerUserId);
                var dguidValue = Guid.NewGuid();
                var provider = _db.Database.ProviderName ?? string.Empty;
                var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
                var fieldGenerationSql = ResolveFieldGenerationSql(payload.FieldGenerationSql, payload.SqlText, provider);
                AddParameter(command, "@company", context.CompanyId.HasValue ? (isSqlite ? context.CompanyId.Value.ToString() : context.CompanyId.Value) : null);
                AddParameter(command, "@dguid", isSqlite ? dguidValue.ToString("N") : dguidValue);
                AddParameter(command, "@name", payload.Name.Trim());
                AddParameter(command, "@sql", payload.SqlText.Trim());
                AddParameter(command, "@fieldSql", string.IsNullOrWhiteSpace(fieldGenerationSql) ? DBNull.Value : fieldGenerationSql);
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
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("DatabaseQuerySelector.Create", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Unable to create record." }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostUpdateAsync([FromBody] QueryPayload payload)
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

            if (payload.Id <= 0 || string.IsNullOrWhiteSpace(payload.Name) || string.IsNullOrWhiteSpace(payload.SqlText))
            {
                return new JsonResult(new { success = false, message = "Please provide a name and SQL." }) { StatusCode = 400 };
            }

            await EnsureTableAsync();
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = @"UPDATE DatabaseQuerySelectors
	SET QueryName = @name, SqlText = @sql, FieldGenerationSql = @fieldSql, UpdatedAtUtc = @updated
	WHERE Id = @id AND OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
                var provider = _db.Database.ProviderName ?? string.Empty;
                var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
                var fieldGenerationSql = ResolveFieldGenerationSql(payload.FieldGenerationSql, payload.SqlText, provider);
                AddParameter(command, "@name", payload.Name.Trim());
                AddParameter(command, "@sql", payload.SqlText.Trim());
                AddParameter(command, "@fieldSql", string.IsNullOrWhiteSpace(fieldGenerationSql) ? DBNull.Value : fieldGenerationSql);
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
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("DatabaseQuerySelector.Update", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
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

            await using (var command = connection.CreateCommand())
            {
                var provider = _db.Database.ProviderName ?? string.Empty;
                var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
                command.CommandText = "DELETE FROM DatabaseQuerySelectors WHERE Id = @id AND OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
                AddParameter(command, "@id", payload.Id);
                AddParameter(command, "@owner", context.OwnerUserId);
                AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
                await command.ExecuteNonQueryAsync();
            }

            return new JsonResult(new { success = true, message = "Record deleted." });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("DatabaseQuerySelector.Delete", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
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
            await EnsureDguidValuesAsync(context.OwnerUserId, context.CompanyId);

            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            var provider = _db.Database.ProviderName ?? string.Empty;
            var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE DatabaseQuerySelectors SET CompanyId=@company, UpdatedAtUtc=@updated WHERE OwnerUserId=@owner AND CompanyId IS NULL";
            AddParameter(command, "@owner", context.OwnerUserId);
            AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
            AddParameter(command, "@updated", DateTime.UtcNow);
            var repaired = await command.ExecuteNonQueryAsync();

            return new JsonResult(new { success = true, repaired, message = $"Database synced. {repaired} record(s) repaired." });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("DatabaseQuerySelector.DatabaseSync", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Unable to sync database." }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostGenerateFieldSqlAsync([FromBody] FieldGenerationPayload payload)
    {
        try
        {
            var context = await GetAccessContextAsync();
            if (context == null)
            {
                return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
            }

            var provider = _db.Database.ProviderName ?? string.Empty;
            var tableName = ResolveTableName(payload.TableName, payload.SqlText);
            if (string.IsNullOrWhiteSpace(tableName))
            {
                return new JsonResult(new { success = false, message = "Unable to detect table name from SQL." }) { StatusCode = 400 };
            }

            var fieldGenerationSql = BuildFieldGenerationSql(tableName, provider);
            var columns = await GetColumnsFromFieldGenerationSqlAsync(fieldGenerationSql);
            return new JsonResult(new
            {
                success = true,
                fieldGenerationSql,
                tableName,
                columns
            });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("DatabaseQuerySelector.GenerateFieldSql", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Unable to generate field SQL." }) { StatusCode = 500 };
        }
    }

    private async Task<List<QueryRow>> LoadQueriesAsync(string ownerUserId, Guid? companyId)
    {
        var list = new List<QueryRow>();
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
                ? "SELECT Id, CompanyId, DGUID, QueryName, SqlText, FieldGenerationSql, CreatedAtUtc, UpdatedAtUtc FROM DatabaseQuerySelectors WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) ORDER BY CreatedAtUtc DESC"
                : "SELECT Id, CompanyId, DGUID, QueryName, SqlText, FieldGenerationSql, CreatedAtUtc, UpdatedAtUtc FROM DatabaseQuerySelectors ORDER BY CreatedAtUtc DESC";
            if (scoped)
            {
                AddParameter(command, "@owner", ownerUserId);
                AddCompanyParameter(command, "@company", companyId, isSqlite);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new QueryRow
                {
                    Id = reader.GetInt32(0),
                    CompanyId = reader.IsDBNull(1) ? string.Empty : (isSqlite ? reader.GetString(1) : reader.GetGuid(1).ToString()),
                    Dguid = isSqlite ? reader.GetString(2) : reader.GetGuid(2).ToString(),
                    QueryName = reader.GetString(3),
                    SqlText = reader.GetString(4),
                    FieldGenerationSql = NormalizeDisplaySql(reader.IsDBNull(5) ? string.Empty : reader.GetString(5)),
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

    private async Task EnsureTableAsync()
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
                async Task<HashSet<string>> LoadColumnsAsync()
                {
                    var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    await using var pragma = connection.CreateCommand();
                    pragma.CommandText = "PRAGMA table_info(\"DatabaseQuerySelectors\")";
                    await using var reader = await pragma.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        columns.Add(reader.GetString(1));
                    }
                    return columns;
                }

                var columns = await LoadColumnsAsync();
                if (!columns.Contains("QueryName"))
                {
                    if (columns.Contains("Name"))
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

                columns = await LoadColumnsAsync();
                if (!columns.Contains("FieldGenerationSql"))
                {
                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE DatabaseQuerySelectors ADD COLUMN FieldGenerationSql TEXT;");
                }
                if (!columns.Contains("CompanyId"))
                {
                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE DatabaseQuerySelectors ADD COLUMN CompanyId TEXT NULL;");
                }

                var hasDguid = false;
                columns = await LoadColumnsAsync();
                var hasName = columns.Contains("Name");
                hasDguid = columns.Contains("DGUID");
                if (!hasDguid)
                {
                    // SQLite cannot add a column with a non-constant default.
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

    private async Task EnsureDguidValuesAsync(string ownerUserId, Guid? companyId)
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

    public class QueryRow
    {
        public int Id { get; set; }
        public string CompanyId { get; set; } = string.Empty;
        public string Dguid { get; set; } = string.Empty;
        public string QueryName { get; set; } = string.Empty;
        public string SqlText { get; set; } = string.Empty;
        public string FieldGenerationSql { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    private static Guid ResolveDguid(string? value)
    {
        return Guid.TryParse(value, out var parsed) ? parsed : Guid.NewGuid();
    }

    public class QueryPayload
    {
        public int Id { get; set; }
        public string? Dguid { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string SqlText { get; set; } = string.Empty;

        public string? FieldGenerationSql { get; set; }
    }

    public class DeletePayload
    {
        public int Id { get; set; }
    }

    public class FieldGenerationPayload
    {
        public string? SqlText { get; set; }
        public string? TableName { get; set; }
    }

    private static string ResolveFieldGenerationSql(string? explicitFieldSql, string sqlText, string provider)
    {
        if (!string.IsNullOrWhiteSpace(explicitFieldSql))
        {
            return explicitFieldSql.Trim();
        }

        var tableName = ResolveTableName(null, sqlText);
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return string.Empty;
        }

        return BuildFieldGenerationSql(tableName, provider);
    }

    private static string NormalizeDisplaySql(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed == "-" || trimmed == "\u2014" || trimmed == "\u00E2\u20AC\u201D")
        {
            return string.Empty;
        }

        return value;
    }

    private static string ResolveTableName(string? explicitTableName, string? sqlText)
    {
        if (!string.IsNullOrWhiteSpace(explicitTableName))
        {
            return explicitTableName.Trim();
        }

        if (string.IsNullOrWhiteSpace(sqlText))
        {
            return string.Empty;
        }

        var match = Regex.Match(sqlText, @"\bfrom\s+([A-Za-z0-9_\.\[\]`""]+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return string.Empty;
        }

        return match.Groups[1].Value.Trim();
    }

    private static string BuildFieldGenerationSql(string rawTableName, string provider)
    {
        var safeTableName = BuildSafeTableIdentifier(rawTableName, provider);
        if (string.IsNullOrWhiteSpace(safeTableName))
        {
            return string.Empty;
        }

        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        return isSqlite
            ? $"SELECT * FROM {safeTableName} LIMIT 1"
            : $"SELECT TOP 1 * FROM {safeTableName}";
    }

    private static string BuildSafeTableIdentifier(string rawTableName, string provider)
    {
        if (string.IsNullOrWhiteSpace(rawTableName))
        {
            return string.Empty;
        }

        var normalized = rawTableName.Trim().Trim('[', ']', '"', '`');
        var segments = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return string.Empty;
        }

        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var quoted = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            var cleaned = segment.Trim().Trim('[', ']', '"', '`');
            if (!Regex.IsMatch(cleaned, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            {
                return string.Empty;
            }

            quoted.Add(isSqlite ? $"\"{cleaned}\"" : $"[{cleaned}]");
        }

        return string.Join(".", quoted);
    }

    private async Task<List<string>> GetColumnsFromFieldGenerationSqlAsync(string fieldGenerationSql)
    {
        var columns = new List<string>();
        if (string.IsNullOrWhiteSpace(fieldGenerationSql))
        {
            return columns;
        }

        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = fieldGenerationSql;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly | CommandBehavior.SingleResult);
        var schema = reader.GetColumnSchema();
        foreach (var column in schema)
        {
            if (!string.IsNullOrWhiteSpace(column.ColumnName))
            {
                columns.Add(column.ColumnName!);
            }
        }

        return columns;
    }
}


