using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
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
public class TableModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public TableModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [BindProperty(SupportsGet = true)]
    public Guid TableId { get; set; }

    public string UserName { get; private set; } = "Administrator";
    public string UserEmail { get; private set; } = "—";
    public string UserInitials { get; private set; } = "AD";
    public bool CanManage { get; private set; }
    public string? TableName { get; private set; }
    public List<ColumnMeta> Columns { get; private set; } = new();
    public List<RecordRow> Records { get; private set; } = new();
    public string RecordsJson { get; private set; } = "[]";
    public string? ErrorMessage { get; private set; }
    public int? CurrentCompanyOrdinalId { get; private set; }
    public string? CurrentCompanyGuid { get; private set; }

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

        if (TableId == Guid.Empty)
        {
            ErrorMessage = "Invalid table reference.";
            return Page();
        }

        await EnsureMetadataTablesAsync();
        var tableMeta = await LoadTableMetaAsync(TableId, context.OwnerUserId);
        if (tableMeta == null)
        {
            ErrorMessage = "Table not found.";
            return Page();
        }

        var tablePermission = await ResolveTablePermissionAsync(context, TableId);
        if (!tablePermission.CanView)
        {
            return Forbid();
        }

        TableName = tableMeta.TableName;
        CanManage = tablePermission.CanManage;
        Columns = tableMeta.Columns;
        if (context.CompanyId.HasValue)
        {
            CurrentCompanyOrdinalId = await GetCompanyOrdinalIdAsync(context.CompanyId.Value);
            CurrentCompanyGuid = context.CompanyId.Value.ToString();
        }
        Records = await LoadRecordsAsync(TableId, tableMeta.TableName, tableMeta.Columns);
        RecordsJson = JsonSerializer.Serialize(Records, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        return Page();
    }

    private async Task<List<RecordRow>> LoadRecordsAsync(Guid tableId, string tableName, List<ColumnMeta> columns)
    {
        var rows = new List<RecordRow>();
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RecordId, DGUID, CreatedAtUtc FROM ApplicationRecords WHERE ApplicationTableId = @id ORDER BY RecordId DESC";
        AddParameter(command, "@id", tableId);
        await using var reader = await command.ExecuteReaderAsync();
        var recordIds = new List<RecordRow>();
        while (await reader.ReadAsync())
        {
            recordIds.Add(new RecordRow
            {
                RecordId = reader.GetInt32(0),
                Dguid = isSqlite ? Guid.Parse(reader.GetString(1)) : reader.GetGuid(1),
                CreatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(2)) : reader.GetDateTime(2)
            });
        }
        await reader.CloseAsync();

        if (recordIds.Count == 0)
        {
            return rows;
        }

        var inParams = recordIds.Select((r, i) => $"@p{i}").ToList();
        var columnNames = columns
            .Where(c => !string.Equals(c.Name, "DGUID", StringComparison.OrdinalIgnoreCase))
            .Select(c => isSqlite ? $"\"{c.Name}\"" : $"[{c.Name}]")
            .ToList();
        var tableIdentifier = isSqlite ? $"\"{tableName}\"" : $"[{tableName}]";
        var selectCols = string.Join(", ", new[] { isSqlite ? "\"DGUID\"" : "[DGUID]" }.Concat(columnNames));

        await using var dataCmd = connection.CreateCommand();
        dataCmd.CommandText = $"SELECT {selectCols} FROM {tableIdentifier} WHERE {(isSqlite ? "\"DGUID\"" : "[DGUID]")} IN ({string.Join(", ", inParams)})";
        for (var i = 0; i < recordIds.Count; i++)
        {
            AddParameter(dataCmd, inParams[i], recordIds[i].Dguid);
        }

        var valuesByGuid = new Dictionary<Guid, Dictionary<string, object?>>();
        await using var dataReader = await dataCmd.ExecuteReaderAsync();
        while (await dataReader.ReadAsync())
        {
            var dguid = isSqlite ? Guid.Parse(dataReader.GetString(0)) : dataReader.GetGuid(0);
            var valueDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < dataReader.FieldCount; i++)
            {
                var name = dataReader.GetName(i);
                valueDict[name] = dataReader.IsDBNull(i) ? null : dataReader.GetValue(i);
            }
            valuesByGuid[dguid] = valueDict;
        }

        foreach (var record in recordIds)
        {
            record.Values = valuesByGuid.TryGetValue(record.Dguid, out var vals) ? vals : new Dictionary<string, object?>();
            rows.Add(record);
        }

        return rows;
    }

    public async Task<IActionResult> OnPostCreateRecordAsync()
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
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

        var tablePermission = await ResolveTablePermissionAsync(context, tableId);
        if (!tablePermission.CanManage)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to create records." }) { StatusCode = 403 };
        }

        var values = await ExtractValuesAsync(form, tableMeta.Columns, tableMeta.TableName, null, context.CompanyId);
        if (!values.Success)
        {
            return new JsonResult(new { success = false, message = values.Message }) { StatusCode = 400 };
        }

        var dguid = Guid.NewGuid();
        var sql = BuildInsertSql(tableMeta.TableName, tableMeta.Columns);

        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = sql;
            AddParameter(cmd, "@DGUID", dguid);
            foreach (var column in tableMeta.Columns.Where(c => !string.Equals(c.Name, "DGUID", StringComparison.OrdinalIgnoreCase)))
            {
                AddParameter(cmd, $"@{column.Name}", values.Data[column.Name]);
            }
            var inserted = await cmd.ExecuteNonQueryAsync();
            if (inserted <= 0)
            {
                return new JsonResult(new { success = false, message = "Unable to create record." }) { StatusCode = 500 };
            }
        }

        var recordId = await InsertRecordMetaAsync((DbConnection)connection, tableId, dguid);
        return new JsonResult(new { success = true, recordId });
    }

    public async Task<IActionResult> OnGetNextIdAsync([FromQuery] Guid tableId)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        if (tableId == Guid.Empty)
        {
            return new JsonResult(new { success = false, message = "Invalid table reference." }) { StatusCode = 400 };
        }

        await EnsureMetadataTablesAsync();
        var tableMeta = await LoadTableMetaAsync(tableId, context.OwnerUserId);
        if (tableMeta == null)
        {
            return new JsonResult(new { success = false, message = "Table not found." }) { StatusCode = 404 };
        }

        var tablePermission = await ResolveTablePermissionAsync(context, tableId);
        if (!tablePermission.CanManage)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to create records." }) { StatusCode = 403 };
        }

        var idColumn = tableMeta.Columns.FirstOrDefault(c => c.Name.Equals("ID", StringComparison.OrdinalIgnoreCase));
        if (idColumn == null)
        {
            return new JsonResult(new { success = false, message = "ID column not found." }) { StatusCode = 400 };
        }

        if (!IsNumericType(idColumn.Type))
        {
            return new JsonResult(new { success = false, message = "ID must be numeric." }) { StatusCode = 400 };
        }

        var nextId = await GetNextIdAsync(tableMeta.TableName, idColumn.Name, idColumn.Type);
        return new JsonResult(new { success = true, nextId });
    }

    public async Task<IActionResult> OnGetRecordAsync([FromQuery] int recordId)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        if (recordId <= 0)
        {
            return new JsonResult(new { success = false, message = "Invalid record." }) { StatusCode = 400 };
        }

        var recordMeta = await LoadRecordMetaAsync(recordId);
        if (recordMeta == null)
        {
            return new JsonResult(new { success = false, message = "Record not found." }) { StatusCode = 404 };
        }

        var tableMeta = await LoadTableMetaAsync(recordMeta.TableId, context.OwnerUserId);
        if (tableMeta == null)
        {
            return new JsonResult(new { success = false, message = "Table not found." }) { StatusCode = 404 };
        }

        var tablePermission = await ResolveTablePermissionAsync(context, recordMeta.TableId);
        if (!tablePermission.CanView)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to view this record." }) { StatusCode = 403 };
        }

        var values = await LoadRecordValuesAsync(tableMeta.TableName, recordMeta.Dguid);
        return new JsonResult(new { success = true, recordId, values });
    }

    public async Task<IActionResult> OnPostUpdateRecordAsync()
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        var form = await Request.ReadFormAsync();
        if (!int.TryParse(form["recordId"], out var recordId))
        {
            return new JsonResult(new { success = false, message = "Invalid record." }) { StatusCode = 400 };
        }

        var recordMeta = await LoadRecordMetaAsync(recordId);
        if (recordMeta == null)
        {
            return new JsonResult(new { success = false, message = "Record not found." }) { StatusCode = 404 };
        }

        var tableMeta = await LoadTableMetaAsync(recordMeta.TableId, context.OwnerUserId);
        if (tableMeta == null)
        {
            return new JsonResult(new { success = false, message = "Table not found." }) { StatusCode = 404 };
        }

        var tablePermission = await ResolveTablePermissionAsync(context, recordMeta.TableId);
        if (!tablePermission.CanManage)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to update records." }) { StatusCode = 403 };
        }

        var values = await ExtractValuesAsync(form, tableMeta.Columns, tableMeta.TableName, recordMeta.Dguid, context.CompanyId);
        if (!values.Success)
        {
            return new JsonResult(new { success = false, message = values.Message }) { StatusCode = 400 };
        }

        var sql = BuildUpdateSql(tableMeta.TableName, tableMeta.Columns);
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = sql;
            AddParameter(cmd, "@DGUID", recordMeta.Dguid);
            foreach (var column in tableMeta.Columns.Where(c => !string.Equals(c.Name, "DGUID", StringComparison.OrdinalIgnoreCase)))
            {
                AddParameter(cmd, $"@{column.Name}", values.Data[column.Name]);
            }
            var updated = await cmd.ExecuteNonQueryAsync();
            if (updated <= 0)
            {
                return new JsonResult(new { success = false, message = "Record not found or not updated." }) { StatusCode = 404 };
            }
        }

        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostDeleteRecordAsync([FromBody] DeleteRecordPayload payload)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        var recordMeta = await LoadRecordMetaAsync(payload.RecordId);
        if (recordMeta == null)
        {
            return new JsonResult(new { success = false, message = "Record not found." }) { StatusCode = 404 };
        }

        var tableMeta = await LoadTableMetaAsync(recordMeta.TableId, context.OwnerUserId);
        if (tableMeta == null)
        {
            return new JsonResult(new { success = false, message = "Table not found." }) { StatusCode = 404 };
        }

        var tablePermission = await ResolveTablePermissionAsync(context, recordMeta.TableId);
        if (!tablePermission.CanManage)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to delete records." }) { StatusCode = 403 };
        }

        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var tableIdentifier = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? $"\"{tableMeta.TableName}\""
            : $"[{tableMeta.TableName}]";
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"DELETE FROM {tableIdentifier} WHERE {(provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "\"DGUID\"" : "[DGUID]")} = @dguid";
            AddParameter(cmd, "@dguid", recordMeta.Dguid);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var delMeta = connection.CreateCommand())
        {
            delMeta.CommandText = "DELETE FROM ApplicationRecords WHERE RecordId = @id";
            AddParameter(delMeta, "@id", payload.RecordId);
            await delMeta.ExecuteNonQueryAsync();
        }

        return new JsonResult(new { success = true });
    }

    private async Task<TableMeta?> LoadTableMetaAsync(Guid tableId, string ownerUserId)
    {
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        await using var tableCmd = connection.CreateCommand();
        tableCmd.CommandText = "SELECT TableName FROM ApplicationTables WHERE Id = @id AND OwnerUserId = @owner";
        AddParameter(tableCmd, "@id", tableId);
        AddParameter(tableCmd, "@owner", ownerUserId);
        var nameObj = await tableCmd.ExecuteScalarAsync();
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

    private async Task EnsureMetadataTablesAsync()
    {
        await ApplicationTablePermissionService.EnsurePermissionsTableAsync(_db);

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

    private async Task<TableAccessContext?> GetAccessContextAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return null;
        }
        var scope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);

        return new TableAccessContext
        {
            User = user,
            OwnerUserId = scope.OwnerUserId,
            CanManage = scope.CanManage,
            TeamRole = scope.TeamRole,
            CompanyId = scope.CompanyId
        };
    }

    private async Task<ApplicationTablePermissionService.EffectivePermission> ResolveTablePermissionAsync(TableAccessContext context, Guid tableId)
    {
        var explicitAccess = await ApplicationTablePermissionService.GetExplicitPermissionAsync(
            _db,
            context.OwnerUserId,
            tableId,
            context.User.Id);

        return ApplicationTablePermissionService.ResolveEffectivePermission(
            context.User.Id,
            context.OwnerUserId,
            context.TeamRole,
            explicitAccess);
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

    private sealed class TableAccessContext
    {
        public required ApplicationUser User { get; init; }
        public required string OwnerUserId { get; init; }
        public bool CanManage { get; init; }
        public string? TeamRole { get; init; }
        public Guid? CompanyId { get; init; }
    }

    private record TableMeta(string TableName, List<ColumnMeta> Columns);

    private record RecordMeta(Guid TableId, Guid Dguid);

    public class ColumnMeta
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int? Length { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public bool IsNullable { get; set; }
    }

    public class RecordRow
    {
        public int RecordId { get; set; }
        public Guid Dguid { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public Dictionary<string, object?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class DeleteRecordPayload
    {
        public int RecordId { get; set; }
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
        command.CommandText = $"SELECT * FROM {tableIdentifier} WHERE {(provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "\"DGUID\"" : "[DGUID]")} = @dguid";
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

    private async Task<(bool Success, string? Message, Dictionary<string, object?> Data)> ExtractValuesAsync(IFormCollection form, List<ColumnMeta> columns, string tableName, Guid? existingDguid = null, Guid? companyId = null)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns.Where(c => !string.Equals(c.Name, "DGUID", StringComparison.OrdinalIgnoreCase)))
        {
            if (column.Name.Equals("CompanyID", StringComparison.OrdinalIgnoreCase))
            {
                var companyValue = await ResolveCompanyColumnValueAsync(column, companyId, tableName, existingDguid);
                if (companyValue is null && !column.IsNullable)
                {
                    return (false, "Company scope is required for this table.", values);
                }

                values[column.Name] = companyValue;
                continue;
            }

            if (column.Name.Equals("ID", StringComparison.OrdinalIgnoreCase))
            {
                if (existingDguid.HasValue)
                {
                    var existing = await LoadRecordValuesAsync(tableName, existingDguid.Value);
                    values[column.Name] = existing.TryGetValue(column.Name, out var oldVal) ? oldVal : null;
                }
                else
                {
                    values[column.Name] = await GetNextIdAsync(tableName, column.Name, column.Type);
                }
                continue;
            }

            if (IsImageColumn(column))
            {
                var file = form.Files.GetFile(column.Name);
                if (file == null || file.Length == 0)
                {
                    if (existingDguid.HasValue)
                    {
                        var existing = await LoadRecordValuesAsync(tableName, existingDguid.Value);
                        values[column.Name] = existing.TryGetValue(column.Name, out var oldVal) ? oldVal : null;
                        continue;
                    }

                    if (!column.IsNullable)
                    {
                        return (false, $"Image column \"{column.Name}\" is required.", values);
                    }

                    values[column.Name] = null;
                    continue;
                }

                var safeName = Path.GetFileName(file.FileName);
                var folder = Path.Combine("wwwroot", "uploads", "applications", tableName);
                Directory.CreateDirectory(folder);
                var fileName = $"{Guid.NewGuid():N}_{safeName}";
                var filePath = Path.Combine(folder, fileName);
                await using (var stream = System.IO.File.Create(filePath))
                {
                    await file.CopyToAsync(stream);
                }
                values[column.Name] = $"/uploads/applications/{tableName}/{fileName}";
                continue;
            }

            var raw = form[column.Name].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (existingDguid.HasValue)
                {
                    var existing = await LoadRecordValuesAsync(tableName, existingDguid.Value);
                    values[column.Name] = existing.TryGetValue(column.Name, out var oldVal) ? oldVal : null;
                    continue;
                }

                if (!column.IsNullable)
                {
                    return (false, $"Column \"{column.Name}\" is required.", values);
                }
                values[column.Name] = null;
                continue;
            }

            if (!TryConvertValue(raw, column, out var parsed, out var error))
            {
                return (false, error, values);
            }

            values[column.Name] = parsed;
        }

        return (true, null, values);
    }

    private async Task<object?> ResolveCompanyColumnValueAsync(ColumnMeta column, Guid? companyId, string tableName, Guid? existingDguid)
    {
        if (existingDguid.HasValue)
        {
            var existing = await LoadRecordValuesAsync(tableName, existingDguid.Value);
            if (existing.TryGetValue(column.Name, out var existingValue) && existingValue != null)
            {
                return existingValue;
            }
        }

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

    private static bool IsImageColumn(ColumnMeta column)
    {
        return column.Type.Equals("image", StringComparison.OrdinalIgnoreCase)
               || column.Name.Contains("image", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<object?> GetNextIdAsync(string tableName, string columnName, string columnType)
    {
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var tableIdentifier = isSqlite ? $"\"{tableName}\"" : $"[{tableName}]";
        var columnIdentifier = isSqlite ? $"\"{columnName}\"" : $"[{columnName}]";
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT MAX({columnIdentifier}) FROM {tableIdentifier}";
        var result = await command.ExecuteScalarAsync();
        if (result == null || result is DBNull)
        {
            return columnType.Equals("bigint", StringComparison.OrdinalIgnoreCase) ? 1L : 1;
        }

        if (columnType.Equals("bigint", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToInt64(result) + 1;
        }

        if (columnType.Equals("decimal", StringComparison.OrdinalIgnoreCase) || columnType.Equals("numeric", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToDecimal(result) + 1;
        }

        return Convert.ToInt32(result) + 1;
    }

    private static bool IsNumericType(string columnType)
    {
        return columnType.Equals("int", StringComparison.OrdinalIgnoreCase)
               || columnType.Equals("bigint", StringComparison.OrdinalIgnoreCase)
               || columnType.Equals("decimal", StringComparison.OrdinalIgnoreCase)
               || columnType.Equals("numeric", StringComparison.OrdinalIgnoreCase);
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
            case "numeric":
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
        columnNames.AddRange(columns.Where(c => !string.Equals(c.Name, "DGUID", StringComparison.OrdinalIgnoreCase)).Select(c => $"{quote}{c.Name}{quoteEnd}"));
        var values = new List<string> { "@DGUID" };
        values.AddRange(columns.Where(c => !string.Equals(c.Name, "DGUID", StringComparison.OrdinalIgnoreCase)).Select(c => $"@{c.Name}"));

        var tableIdentifier = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? $"\"{tableName}\""
            : $"[{tableName}]";

        return $"INSERT INTO {tableIdentifier} ({string.Join(", ", columnNames)}) VALUES ({string.Join(", ", values)})";
    }

    private string BuildUpdateSql(string tableName, List<ColumnMeta> columns)
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        var quote = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "\"" : "[";
        var quoteEnd = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "\"" : "]";
        var sets = columns.Where(c => !string.Equals(c.Name, "DGUID", StringComparison.OrdinalIgnoreCase))
            .Select(c => $"{quote}{c.Name}{quoteEnd}=@{c.Name}");
        var tableIdentifier = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? $"\"{tableName}\""
            : $"[{tableName}]";
        return $"UPDATE {tableIdentifier} SET {string.Join(", ", sets)} WHERE {quote}DGUID{quoteEnd}=@DGUID";
    }

    private async Task<int> InsertRecordMetaAsync(DbConnection connection, Guid tableId, Guid dguid)
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var createdAt = DateTime.UtcNow;

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO ApplicationRecords (ApplicationTableId, DGUID, CreatedAtUtc) VALUES (@tableId, @dguid, @created)";
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
}
