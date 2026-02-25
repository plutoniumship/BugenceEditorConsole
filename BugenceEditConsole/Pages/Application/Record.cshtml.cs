using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Common;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Application;

[Authorize]
public class RecordModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _environment;

    public RecordModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IWebHostEnvironment environment)
    {
        _db = db;
        _userManager = userManager;
        _environment = environment;
    }

    [BindProperty(SupportsGet = true)]
    public int RecordId { get; set; }

    public string UserName { get; private set; } = "Administrator";
    public string UserEmail { get; private set; } = "—";
    public string UserInitials { get; private set; } = "AD";
    public bool CanManage { get; private set; }
    public bool RecordFound { get; private set; }
    public string? TableName { get; private set; }
    public Guid? TableId { get; private set; }
    public Guid? RecordGuid { get; private set; }
    public List<ColumnMeta> Columns { get; private set; } = new();
    public Dictionary<string, object?> Values { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? ErrorMessage { get; private set; }

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

        if (RecordId <= 0)
        {
            ErrorMessage = "Invalid record id.";
            return Page();
        }

        await EnsureMetadataTablesAsync();
        var recordMeta = await LoadRecordMetaAsync(RecordId);
        if (recordMeta == null)
        {
            ErrorMessage = "Record not found.";
            return Page();
        }

        RecordFound = true;
        TableId = recordMeta.TableId;
        RecordGuid = recordMeta.Dguid;

        var tableMeta = await LoadTableMetaAsync(recordMeta.TableId, context.OwnerUserId);
        if (tableMeta == null)
        {
            ErrorMessage = "Table not found.";
            RecordFound = false;
            return Page();
        }

        TableName = tableMeta.TableName;
        Columns = tableMeta.Columns;
        Values = await LoadRecordValuesAsync(tableMeta.TableName, recordMeta.Dguid);

        return Page();
    }

    public async Task<IActionResult> OnPostCreateRecordAsync()
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

        var form = await Request.ReadFormAsync();
        if (!Guid.TryParse(form["tableId"], out var tableId))
        {
            return new JsonResult(new { success = false, message = "Invalid table reference." }) { StatusCode = 400 };
        }

        await EnsureMetadataTablesAsync();
        var tableMeta = await LoadTableMetaAsync(tableId, context.OwnerUserId);
        if (tableMeta == null)
        {
            return new JsonResult(new { success = false, message = "Table not found." }) { StatusCode = 404 };
        }

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in tableMeta.Columns)
        {
            if (column.Name.Equals("CompanyID", StringComparison.OrdinalIgnoreCase))
            {
                var companyValue = await ResolveCompanyColumnValueAsync(column, context.CompanyId);
                if (companyValue is null && !column.IsNullable)
                {
                    return new JsonResult(new { success = false, message = "Company scope is required for this table." }) { StatusCode = 400 };
                }

                values[column.Name] = companyValue;
                continue;
            }

            if (string.Equals(column.Type, "image", StringComparison.OrdinalIgnoreCase))
            {
                var file = form.Files.GetFile(column.Name);
                if (file == null || file.Length == 0)
                {
                    if (!column.IsNullable)
                    {
                        return new JsonResult(new { success = false, message = $"Image column \"{column.Name}\" is required." }) { StatusCode = 400 };
                    }
                    values[column.Name] = null;
                    continue;
                }

                var safeName = Path.GetFileName(file.FileName);
                var folder = Path.Combine(_environment.WebRootPath ?? "wwwroot", "uploads", "applications", tableMeta.TableName);
                Directory.CreateDirectory(folder);
                var fileName = $"{Guid.NewGuid():N}_{safeName}";
                var filePath = Path.Combine(folder, fileName);
                await using (var stream = System.IO.File.Create(filePath))
                {
                    await file.CopyToAsync(stream);
                }
                values[column.Name] = $"/uploads/applications/{tableMeta.TableName}/{fileName}";
                continue;
            }

            var raw = form[column.Name].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (!column.IsNullable)
                {
                    return new JsonResult(new { success = false, message = $"Column \"{column.Name}\" is required." }) { StatusCode = 400 };
                }
                values[column.Name] = null;
                continue;
            }

            if (!TryConvertValue(raw, column, out var parsed, out var error))
            {
                return new JsonResult(new { success = false, message = error }) { StatusCode = 400 };
            }

            values[column.Name] = parsed;
        }

        var dguid = Guid.NewGuid();
        var sql = BuildInsertSql(tableMeta.TableName, tableMeta.Columns);

        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = sql;
            AddParameter(command, "@DGUID", dguid);
            foreach (var column in tableMeta.Columns)
            {
                AddParameter(command, $"@{column.Name}", values[column.Name]);
            }
            var inserted = await command.ExecuteNonQueryAsync();
            if (inserted <= 0)
            {
                return new JsonResult(new { success = false, message = "Unable to create record." }) { StatusCode = 500 };
            }
        }

        var recordId = await InsertRecordMetaAsync(connection, tableId, dguid);
        return new JsonResult(new { success = true, recordId });
    }

    private async Task<object?> ResolveCompanyColumnValueAsync(ColumnMeta column, Guid? companyId)
    {
        if (!companyId.HasValue)
        {
            return null;
        }

        if (IsNumericType(column.Type))
        {
            return await GetCompanyOrdinalIdAsync(companyId.Value);
        }

        return companyId.Value.ToString();
    }

    private async Task<int> GetCompanyOrdinalIdAsync(Guid companyId)
    {
        var orderedCompanyIds = await _db.CompanyProfiles
            .AsNoTracking()
            .OrderBy(c => c.CreatedAtUtc)
            .ThenBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync();

        var index = orderedCompanyIds.FindIndex(id => id == companyId);
        return index >= 0 ? index + 1 : 1;
    }

    private static bool IsNumericType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        return type.Equals("int", StringComparison.OrdinalIgnoreCase)
               || type.Equals("bigint", StringComparison.OrdinalIgnoreCase)
               || type.Equals("decimal", StringComparison.OrdinalIgnoreCase)
               || type.Equals("numeric", StringComparison.OrdinalIgnoreCase)
               || type.Equals("float", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> InsertRecordMetaAsync(DbConnection connection, Guid tableId, Guid dguid)
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var createdAt = DateTime.UtcNow;

        await using (var insert = connection.CreateCommand())
        {
            if (isSqlite)
            {
                insert.CommandText = "INSERT INTO ApplicationRecords (ApplicationTableId, DGUID, CreatedAtUtc) VALUES (@tableId, @dguid, @created)";
            }
            else
            {
                insert.CommandText = "INSERT INTO ApplicationRecords (ApplicationTableId, DGUID, CreatedAtUtc) VALUES (@tableId, @dguid, @created)";
            }
            AddParameter(insert, "@tableId", tableId);
            AddParameter(insert, "@dguid", dguid);
            AddParameter(insert, "@created", createdAt);
            await insert.ExecuteNonQueryAsync();
        }

        await using var scalar = connection.CreateCommand();
        scalar.CommandText = isSqlite ? "SELECT last_insert_rowid()" : "SELECT CAST(SCOPE_IDENTITY() as int)";
        var result = await scalar.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static bool TryConvertValue(string raw, ColumnMeta column, out object? value, out string? error)
    {
        error = null;
        value = raw;
        var type = column.Type.ToLowerInvariant();
        switch (type)
        {
            case "int":
                if (int.TryParse(raw, out var i))
                {
                    value = i;
                    return true;
                }
                error = $"Column \"{column.Name}\" expects an integer.";
                return false;
            case "bigint":
                if (long.TryParse(raw, out var l))
                {
                    value = l;
                    return true;
                }
                error = $"Column \"{column.Name}\" expects a number.";
                return false;
            case "decimal":
                if (decimal.TryParse(raw, out var d))
                {
                    value = d;
                    return true;
                }
                error = $"Column \"{column.Name}\" expects a decimal value.";
                return false;
            case "float":
                if (double.TryParse(raw, out var f))
                {
                    value = f;
                    return true;
                }
                error = $"Column \"{column.Name}\" expects a number.";
                return false;
            case "bit":
                if (raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1")
                {
                    value = true;
                    return true;
                }
                if (raw.Equals("false", StringComparison.OrdinalIgnoreCase) || raw == "0")
                {
                    value = false;
                    return true;
                }
                error = $"Column \"{column.Name}\" expects true/false.";
                return false;
            case "datetime":
                if (DateTime.TryParse(raw, out var dt))
                {
                    value = dt;
                    return true;
                }
                error = $"Column \"{column.Name}\" expects a date/time value.";
                return false;
            case "nvarchar":
            case "varchar":
            case "char":
            default:
                value = raw;
                return true;
        }
    }

    private string BuildInsertSql(string tableName, List<ColumnMeta> columns)
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        var quote = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "\"" : "[";
        var quoteEnd = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "\"" : "]";

        var columnNames = new List<string> { $"{quote}DGUID{quoteEnd}" };
        columnNames.AddRange(columns.Select(c => $"{quote}{c.Name}{quoteEnd}"));
        var values = new List<string> { "@DGUID" };
        values.AddRange(columns.Select(c => $"@{c.Name}"));

        var tableIdentifier = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? $"\"{tableName}\""
            : $"[{tableName}]";

        return $"INSERT INTO {tableIdentifier} ({string.Join(", ", columnNames)}) VALUES ({string.Join(", ", values)})";
    }

    private async Task<RecordMeta?> LoadRecordMetaAsync(int recordId)
    {
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ApplicationTableId, DGUID FROM ApplicationRecords WHERE RecordId = @id";
        AddParameter(command, "@id", recordId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var tableId = isSqlite ? Guid.Parse(reader.GetString(0)) : reader.GetGuid(0);
        var dguid = isSqlite ? Guid.Parse(reader.GetString(1)) : reader.GetGuid(1);
        return new RecordMeta(tableId, dguid);
    }

    private async Task<TableMeta?> LoadTableMetaAsync(Guid tableId, string ownerUserId)
    {
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT TableName FROM ApplicationTables WHERE Id = @id AND OwnerUserId = @owner";
            AddParameter(command, "@id", tableId);
            AddParameter(command, "@owner", ownerUserId);
            var nameObj = await command.ExecuteScalarAsync();
            if (nameObj == null)
            {
                return null;
            }

            var tableName = Convert.ToString(nameObj) ?? string.Empty;
            var columns = new List<ColumnMeta>();

            await using var columnCmd = connection.CreateCommand();
            columnCmd.CommandText = "SELECT ColumnName, DataType, Length, Precision, Scale, IsNullable FROM ApplicationTableColumns WHERE ApplicationTableId = @id ORDER BY ColumnName";
            AddParameter(columnCmd, "@id", tableId);
            await using var reader = await columnCmd.ExecuteReaderAsync();
            var provider = _db.Database.ProviderName ?? string.Empty;
            var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
            while (await reader.ReadAsync())
            {
                columns.Add(new ColumnMeta
                {
                    Name = reader.GetString(0),
                    Type = reader.GetString(1),
                    Length = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    Precision = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    Scale = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    IsNullable = !reader.IsDBNull(5) && (isSqlite ? reader.GetInt32(5) == 1 : reader.GetBoolean(5))
                });
            }

            return new TableMeta(tableName, columns);
        }
    }

    private async Task<Dictionary<string, object?>> LoadRecordValuesAsync(string tableName, Guid dguid)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var tableIdentifier = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? $"\"{tableName}\""
            : $"[{tableName}]";

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {tableIdentifier} WHERE DGUID = @dguid";
        AddParameter(command, "@dguid", dguid);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return values;
        }

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            values[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }

        return values;
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

    private async Task<RecordAccessContext?> GetAccessContextAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return null;
        }
        var scope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);

        return new RecordAccessContext
        {
            User = user,
            OwnerUserId = scope.OwnerUserId,
            CanManage = scope.CanManage,
            CompanyId = scope.CompanyId
        };
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

    private sealed class RecordAccessContext
    {
        public required ApplicationUser User { get; init; }
        public required string OwnerUserId { get; init; }
        public bool CanManage { get; init; }
        public Guid? CompanyId { get; init; }
    }

    private record RecordMeta(Guid TableId, Guid Dguid);

    private record TableMeta(string TableName, List<ColumnMeta> Columns);

    public class ColumnMeta
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int? Length { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public bool IsNullable { get; set; }
    }
}
