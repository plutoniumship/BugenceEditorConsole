using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
public class ApplicationsModel : PageModel
{
    private static readonly Regex SafeIdentifier = new("^[A-Za-z][A-Za-z0-9_]{0,63}$", RegexOptions.Compiled);

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public ApplicationsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public string UserName { get; private set; } = "Administrator";
    public string UserEmail { get; private set; } = "—";
    public string UserInitials { get; private set; } = "AD";
    public bool ShowPermissionOnboarding { get; private set; }
    public bool CanManageApps { get; private set; }
    public List<ApplicationTableRow> Tables { get; private set; } = new();
    public string TablesJson { get; private set; } = "[]";
    private static readonly (string TableName, string DisplayName)[] SystemTables =
    {
        ("Masterpages", "Masterpage"),
        ("Pages", "Pages"),
        ("TempleteViewers", "Templete Viewer"),
        ("DatabaseQuerySelectors", "SQL Query Selector"),
        ("ReactBuilders", "React Builder"),
        ("PagePortlets", "Portlets"),
        ("SystemProperties", "System Properties"),
        ("Permissions", "Permissions")
    };

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
        CanManageApps = context.CanManage;
        ShowPermissionOnboarding = string.Equals(Request.Query["onboarding"], "permissions", StringComparison.OrdinalIgnoreCase)
            && context.IsOwner;

        await EnsureMetadataTablesAsync();
        await EnsurePermissionsSystemTableAsync();
        var systemCreatedAt = await RemoveSystemApplicationMetadataAsync(context.OwnerUserId);
        var systemTables = await LoadSystemTablesAsync(systemCreatedAt);
        var appTables = await LoadTablesAsync(context.OwnerUserId);
        var allTables = systemTables
            .Concat(appTables)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToList();

        var activeMembers = await LoadActivePermissionSubjectsAsync(context.OwnerUserId);
        await ApplicationTablePermissionService.CleanupOrphanedPermissionsAsync(_db, context.OwnerUserId);
        await ApplicationTablePermissionService.BackfillMissingPermissionsAsync(
            _db,
            context.OwnerUserId,
            allTables.Select(t => t.Id).ToList(),
            activeMembers.Select(m => new ApplicationTablePermissionService.TeamSubject(m.UserId, m.Role)).ToList());

        var explicitByTable = await ApplicationTablePermissionService.GetExplicitPermissionsForUserAsync(
            _db,
            context.OwnerUserId,
            context.User.Id,
            allTables.Select(t => t.Id).ToList());

        foreach (var table in allTables)
        {
            explicitByTable.TryGetValue(table.Id, out var explicitAccess);
            var effective = ApplicationTablePermissionService.ResolveEffectivePermission(
                context.User.Id,
                context.OwnerUserId,
                context.TeamRole,
                explicitAccess);
            table.CanView = effective.CanView;
            table.CanManage = effective.CanManage;
            table.EffectiveAccessLevel = effective.EffectiveAccessLevel;
        }

        Tables = allTables.Where(t => t.CanView).ToList();
        for (var i = 0; i < Tables.Count; i++)
        {
            Tables[i].DisplayId = i + 1;
        }
        TablesJson = JsonSerializer.Serialize(Tables, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Default,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] CreateTablePayload payload)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        if (!context.CanManage)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to create applications." }) { StatusCode = 403 };
        }

        var tableName = payload.TableName?.Trim() ?? string.Empty;
        if (!SafeIdentifier.IsMatch(tableName))
        {
            return new JsonResult(new { success = false, message = "Table name must start with a letter and contain only letters, numbers, or underscores." }) { StatusCode = 400 };
        }

        if (tableName.Equals("DGUID", StringComparison.OrdinalIgnoreCase))
        {
            return new JsonResult(new { success = false, message = "Table name cannot be DGUID." }) { StatusCode = 400 };
        }

        var columns = payload.Columns ?? new List<ColumnPayload>();
        columns = columns
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => c with { Name = c.Name.Trim(), OriginalName = c.OriginalName?.Trim() })
            .ToList();

        if (columns.Count == 0)
        {
            return new JsonResult(new { success = false, message = "Please add at least one column." }) { StatusCode = 400 };
        }

        if (columns.Any(c => !SafeIdentifier.IsMatch(c.Name)))
        {
            return new JsonResult(new { success = false, message = "Column names must start with a letter and contain only letters, numbers, or underscores." }) { StatusCode = 400 };
        }

        if (columns.Any(c => c.Name.Equals("DGUID", StringComparison.OrdinalIgnoreCase)))
        {
            return new JsonResult(new { success = false, message = "Column name DGUID is reserved and added automatically." }) { StatusCode = 400 };
        }

        var activeColumns = columns.Where(c => !c.IsDeleted).ToList();
        if (activeColumns.Count == 0)
        {
            return new JsonResult(new { success = false, message = "At least one column must remain." }) { StatusCode = 400 };
        }
        if (activeColumns.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != activeColumns.Count)
        {
            return new JsonResult(new { success = false, message = "Column names must be unique." }) { StatusCode = 400 };
        }

        foreach (var column in columns.Where(c => !c.IsDeleted))
        {
            var validation = ValidateColumn(column);
            if (!validation.IsValid)
            {
                return new JsonResult(new { success = false, message = validation.Message }) { StatusCode = 400 };
            }
        }

        await EnsureMetadataTablesAsync();

        if (await TableExistsAsync(tableName))
        {
            return new JsonResult(new { success = false, message = "A table with that name already exists." }) { StatusCode = 409 };
        }

        var sql = BuildCreateTableSql(tableName, columns);
        await _db.Database.ExecuteSqlRawAsync(sql);

        var tableId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using (var insertTable = connection.CreateCommand())
        {
            insertTable.CommandText = "INSERT INTO ApplicationTables (Id, OwnerUserId, TableName, CreatedAtUtc) VALUES (@id, @owner, @name, @created)";
            AddParameter(insertTable, "@id", tableId);
            AddParameter(insertTable, "@owner", context.OwnerUserId);
            AddParameter(insertTable, "@name", tableName);
            AddParameter(insertTable, "@created", createdAt);
            await insertTable.ExecuteNonQueryAsync();
        }

        foreach (var column in columns)
        {
            await using var insertColumn = connection.CreateCommand();
            insertColumn.CommandText = "INSERT INTO ApplicationTableColumns (Id, ApplicationTableId, ColumnName, DataType, Length, Precision, Scale, IsNullable, CreatedAtUtc) VALUES (@id, @tableId, @name, @type, @length, @precision, @scale, @nullable, @created)";
            AddParameter(insertColumn, "@id", Guid.NewGuid());
            AddParameter(insertColumn, "@tableId", tableId);
            AddParameter(insertColumn, "@name", column.Name);
            AddParameter(insertColumn, "@type", column.Type);
            AddParameter(insertColumn, "@length", column.Length.HasValue ? column.Length.Value : DBNull.Value);
            AddParameter(insertColumn, "@precision", column.Precision.HasValue ? column.Precision.Value : DBNull.Value);
            AddParameter(insertColumn, "@scale", column.Scale.HasValue ? column.Scale.Value : DBNull.Value);
            AddParameter(insertColumn, "@nullable", column.IsNullable);
            AddParameter(insertColumn, "@created", createdAt);
            await insertColumn.ExecuteNonQueryAsync();
        }

        return new JsonResult(new { success = true, message = "Application table created." });
    }

    public async Task<IActionResult> OnPostUpdateAsync([FromBody] UpdateTablePayload payload)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        if (payload.TableId == Guid.Empty)
        {
            return new JsonResult(new { success = false, message = "Invalid table reference." }) { StatusCode = 400 };
        }

        var tableName = payload.TableName?.Trim() ?? string.Empty;
        if (!SafeIdentifier.IsMatch(tableName))
        {
            return new JsonResult(new { success = false, message = "Table name must start with a letter and contain only letters, numbers, or underscores." }) { StatusCode = 400 };
        }

        var columns = payload.Columns ?? new List<ColumnPayload>();
        columns = columns
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => c with { Name = c.Name.Trim() })
            .ToList();

        if (columns.Count == 0)
        {
            return new JsonResult(new { success = false, message = "Please add at least one column." }) { StatusCode = 400 };
        }

        if (columns.Any(c => !SafeIdentifier.IsMatch(c.Name)))
        {
            return new JsonResult(new { success = false, message = "Column names must start with a letter and contain only letters, numbers, or underscores." }) { StatusCode = 400 };
        }

        if (columns.Any(c => c.Name.Equals("DGUID", StringComparison.OrdinalIgnoreCase)))
        {
            return new JsonResult(new { success = false, message = "Column name DGUID is reserved and added automatically." }) { StatusCode = 400 };
        }

        if (columns.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count)
        {
            return new JsonResult(new { success = false, message = "Column names must be unique." }) { StatusCode = 400 };
        }

        foreach (var column in columns)
        {
            var validation = ValidateColumn(column);
            if (!validation.IsValid)
            {
                return new JsonResult(new { success = false, message = validation.Message }) { StatusCode = 400 };
            }
        }

        await EnsureMetadataTablesAsync();

        var existingTable = await LoadTableMetadataAsync(payload.TableId, context.OwnerUserId);
        if (existingTable == null)
        {
            return new JsonResult(new { success = false, message = "Table not found." }) { StatusCode = 404 };
        }

        var canManageTable = await CanManageTableAsync(context, payload.TableId);
        if (!canManageTable)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to update this application." }) { StatusCode = 403 };
        }

        if (!string.Equals(existingTable.TableName, tableName, StringComparison.OrdinalIgnoreCase))
        {
            if (await TableExistsAsync(tableName))
            {
                return new JsonResult(new { success = false, message = "A table with that name already exists." }) { StatusCode = 409 };
            }
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        if (!string.Equals(existingTable.TableName, tableName, StringComparison.OrdinalIgnoreCase))
        {
            var current = isSqlite ? $"\"{existingTable.TableName}\"" : $"[{existingTable.TableName}]";
            var next = isSqlite ? $"\"{tableName}\"" : $"[{tableName}]";
            await using var rename = connection.CreateCommand();
            rename.CommandText = isSqlite
                ? $"ALTER TABLE {current} RENAME TO {next}"
                : $"EXEC sp_rename '{existingTable.TableName}', '{tableName}'";
            await rename.ExecuteNonQueryAsync();
        }

        var existingColumns = existingTable.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            if (column.IsExisting || !string.IsNullOrWhiteSpace(column.OriginalName))
            {
                var originalName = string.IsNullOrWhiteSpace(column.OriginalName) ? column.Name : column.OriginalName!;
                if (!existingColumns.TryGetValue(originalName, out var existing))
                {
                    return new JsonResult(new { success = false, message = $"Column \"{originalName}\" was not found." }) { StatusCode = 400 };
                }

                if (!string.Equals(existing.Type, column.Type, StringComparison.OrdinalIgnoreCase))
                {
                    return new JsonResult(new { success = false, message = $"Column \"{originalName}\" type cannot be changed." }) { StatusCode = 400 };
                }

                if (column.IsDeleted)
                {
                    if (isSqlite)
                    {
                        return new JsonResult(new { success = false, message = "Deleting columns is not supported on SQLite." }) { StatusCode = 400 };
                    }

                    var dropSql = $"ALTER TABLE [{tableName}] DROP COLUMN [{originalName}]";
                    await using var drop = connection.CreateCommand();
                    drop.CommandText = dropSql;
                    await drop.ExecuteNonQueryAsync();

                    await using var deleteMeta = connection.CreateCommand();
                    deleteMeta.CommandText = "DELETE FROM ApplicationTableColumns WHERE ApplicationTableId = @id AND ColumnName = @name";
                    AddParameter(deleteMeta, "@id", payload.TableId);
                    AddParameter(deleteMeta, "@name", originalName);
                    await deleteMeta.ExecuteNonQueryAsync();
                    continue;
                }

                if (!string.Equals(originalName, column.Name, StringComparison.OrdinalIgnoreCase))
                {
                    if (isSqlite)
                    {
                        return new JsonResult(new { success = false, message = "Renaming columns is not supported on SQLite." }) { StatusCode = 400 };
                    }

                    await using var rename = connection.CreateCommand();
                    rename.CommandText = $"EXEC sp_rename '{tableName}.{originalName}', '{column.Name}', 'COLUMN'";
                    await rename.ExecuteNonQueryAsync();

                    await using var updateMeta = connection.CreateCommand();
                    updateMeta.CommandText = "UPDATE ApplicationTableColumns SET ColumnName = @name WHERE ApplicationTableId = @id AND ColumnName = @old";
                    AddParameter(updateMeta, "@name", column.Name);
                    AddParameter(updateMeta, "@id", payload.TableId);
                    AddParameter(updateMeta, "@old", originalName);
                    await updateMeta.ExecuteNonQueryAsync();
                }

                continue;
            }

            var alterSql = BuildAddColumnSql(tableName, column);
            await using var alter = connection.CreateCommand();
            alter.CommandText = alterSql;
            await alter.ExecuteNonQueryAsync();

            await using var insertColumn = connection.CreateCommand();
            insertColumn.CommandText = "INSERT INTO ApplicationTableColumns (Id, ApplicationTableId, ColumnName, DataType, Length, Precision, Scale, IsNullable, CreatedAtUtc) VALUES (@id, @tableId, @name, @type, @length, @precision, @scale, @nullable, @created)";
            AddParameter(insertColumn, "@id", Guid.NewGuid());
            AddParameter(insertColumn, "@tableId", payload.TableId);
            AddParameter(insertColumn, "@name", column.Name);
            AddParameter(insertColumn, "@type", column.Type);
            AddParameter(insertColumn, "@length", column.Length.HasValue ? column.Length.Value : DBNull.Value);
            AddParameter(insertColumn, "@precision", column.Precision.HasValue ? column.Precision.Value : DBNull.Value);
            AddParameter(insertColumn, "@scale", column.Scale.HasValue ? column.Scale.Value : DBNull.Value);
            AddParameter(insertColumn, "@nullable", column.IsNullable);
            AddParameter(insertColumn, "@created", DateTime.UtcNow);
            await insertColumn.ExecuteNonQueryAsync();
        }

        await using (var updateTable = connection.CreateCommand())
        {
            updateTable.CommandText = "UPDATE ApplicationTables SET TableName = @name WHERE Id = @id";
            AddParameter(updateTable, "@name", tableName);
            AddParameter(updateTable, "@id", payload.TableId);
            await updateTable.ExecuteNonQueryAsync();
        }

        return new JsonResult(new { success = true, message = "Application updated." });
    }

    public async Task<IActionResult> OnPostDeleteAsync([FromBody] DeleteTablePayload payload)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        if (payload.TableId == Guid.Empty)
        {
            return new JsonResult(new { success = false, message = "Invalid table reference." }) { StatusCode = 400 };
        }

        await EnsureMetadataTablesAsync();
        var existingTable = await LoadTableMetadataAsync(payload.TableId, context.OwnerUserId);
        if (existingTable == null)
        {
            return new JsonResult(new { success = false, message = "Table not found." }) { StatusCode = 404 };
        }

        var canManageTable = await CanManageTableAsync(context, payload.TableId);
        if (!canManageTable)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to delete this application." }) { StatusCode = 403 };
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var identifier = isSqlite ? $"\"{existingTable.TableName}\"" : $"[{existingTable.TableName}]";
        await using (var drop = connection.CreateCommand())
        {
            drop.CommandText = $"DROP TABLE {identifier}";
            await drop.ExecuteNonQueryAsync();
        }

        await using (var deleteCols = connection.CreateCommand())
        {
            deleteCols.CommandText = "DELETE FROM ApplicationTableColumns WHERE ApplicationTableId = @id";
            AddParameter(deleteCols, "@id", payload.TableId);
            await deleteCols.ExecuteNonQueryAsync();
        }

        await using (var deleteRecords = connection.CreateCommand())
        {
            deleteRecords.CommandText = "DELETE FROM ApplicationRecords WHERE ApplicationTableId = @id";
            AddParameter(deleteRecords, "@id", payload.TableId);
            await deleteRecords.ExecuteNonQueryAsync();
        }

        await using (var deleteTable = connection.CreateCommand())
        {
            deleteTable.CommandText = "DELETE FROM ApplicationTables WHERE Id = @id";
            AddParameter(deleteTable, "@id", payload.TableId);
            await deleteTable.ExecuteNonQueryAsync();
        }

        await using (var deletePermissions = connection.CreateCommand())
        {
            deletePermissions.CommandText = "DELETE FROM ApplicationTablePermissions WHERE OwnerUserId = @owner AND ApplicationTableId = @tableId";
            AddParameter(deletePermissions, "@owner", context.OwnerUserId);
            AddParameter(deletePermissions, "@tableId", payload.TableId);
            await deletePermissions.ExecuteNonQueryAsync();
        }

        return new JsonResult(new { success = true, message = "Application deleted." });
    }

    public async Task<IActionResult> OnPostSyncColumnsAsync([FromBody] SyncColumnsPayload payload)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        if (payload.TableId == Guid.Empty)
        {
            return new JsonResult(new { success = false, message = "Invalid table reference." }) { StatusCode = 400 };
        }

        await EnsureMetadataTablesAsync();
        var existingTable = await LoadTableMetadataAsync(payload.TableId, context.OwnerUserId);
        if (existingTable == null)
        {
            return new JsonResult(new { success = false, message = "Table not found." }) { StatusCode = 404 };
        }

        var canManageTable = await CanManageTableAsync(context, payload.TableId);
        if (!canManageTable)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to sync this application." }) { StatusCode = 403 };
        }

        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var dbColumns = await LoadTableSchemaAsync(connection, existingTable.TableName);
        var dbColumnsByName = dbColumns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var metaByName = existingTable.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var tableIdentifier = isSqlite ? $"\"{existingTable.TableName}\"" : $"[{existingTable.TableName}]";
        var dguidColumn = isSqlite ? "\"DGUID\"" : "[DGUID]";

        if (!dbColumnsByName.ContainsKey("DGUID"))
        {
            var dguidColumnSql = isSqlite
                ? "\"DGUID\" TEXT NOT NULL DEFAULT (lower(hex(randomblob(16))))"
                : "[DGUID] uniqueidentifier NOT NULL DEFAULT NEWID()";
            await using var addDguid = connection.CreateCommand();
            addDguid.CommandText = $"ALTER TABLE {tableIdentifier} ADD {dguidColumnSql}";
            await addDguid.ExecuteNonQueryAsync();
            dbColumns = await LoadTableSchemaAsync(connection, existingTable.TableName);
            dbColumnsByName = dbColumns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var dbColumn in dbColumns)
        {
            if (string.Equals(dbColumn.Name, "DGUID", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (metaByName.TryGetValue(dbColumn.Name, out var meta))
            {
                await using var update = connection.CreateCommand();
                update.CommandText = "UPDATE ApplicationTableColumns SET DataType = @type, Length = @length, Precision = @precision, Scale = @scale, IsNullable = @nullable WHERE ApplicationTableId = @id AND ColumnName = @name";
                var resolvedType = meta.Type.Equals("image", StringComparison.OrdinalIgnoreCase)
                    ? meta.Type
                    : dbColumn.Type;
                AddParameter(update, "@type", resolvedType);
                AddParameter(update, "@length", dbColumn.Length.HasValue ? dbColumn.Length.Value : DBNull.Value);
                AddParameter(update, "@precision", dbColumn.Precision.HasValue ? dbColumn.Precision.Value : DBNull.Value);
                AddParameter(update, "@scale", dbColumn.Scale.HasValue ? dbColumn.Scale.Value : DBNull.Value);
                AddParameter(update, "@nullable", dbColumn.IsNullable);
                AddParameter(update, "@id", payload.TableId);
                AddParameter(update, "@name", dbColumn.Name);
                await update.ExecuteNonQueryAsync();
                continue;
            }

            await using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO ApplicationTableColumns (Id, ApplicationTableId, ColumnName, DataType, Length, Precision, Scale, IsNullable, CreatedAtUtc) VALUES (@id, @tableId, @name, @type, @length, @precision, @scale, @nullable, @created)";
            AddParameter(insert, "@id", Guid.NewGuid());
            AddParameter(insert, "@tableId", payload.TableId);
            AddParameter(insert, "@name", dbColumn.Name);
            AddParameter(insert, "@type", dbColumn.Type);
            AddParameter(insert, "@length", dbColumn.Length.HasValue ? dbColumn.Length.Value : DBNull.Value);
            AddParameter(insert, "@precision", dbColumn.Precision.HasValue ? dbColumn.Precision.Value : DBNull.Value);
            AddParameter(insert, "@scale", dbColumn.Scale.HasValue ? dbColumn.Scale.Value : DBNull.Value);
            AddParameter(insert, "@nullable", dbColumn.IsNullable);
            AddParameter(insert, "@created", DateTime.UtcNow);
            await insert.ExecuteNonQueryAsync();
        }

        foreach (var meta in existingTable.Columns)
        {
            if (!dbColumnsByName.ContainsKey(meta.Name) && !string.Equals(meta.Name, "DGUID", StringComparison.OrdinalIgnoreCase))
            {
                await using var delete = connection.CreateCommand();
                delete.CommandText = "DELETE FROM ApplicationTableColumns WHERE ApplicationTableId = @id AND ColumnName = @name";
                AddParameter(delete, "@id", payload.TableId);
                AddParameter(delete, "@name", meta.Name);
                await delete.ExecuteNonQueryAsync();
            }
        }

        await using (var fillDguid = connection.CreateCommand())
        {
            fillDguid.CommandText = isSqlite
                ? $"UPDATE {tableIdentifier} SET {dguidColumn} = lower(hex(randomblob(16))) WHERE {dguidColumn} IS NULL OR {dguidColumn} = ''"
                : $"UPDATE {tableIdentifier} SET {dguidColumn} = NEWID() WHERE {dguidColumn} IS NULL OR {dguidColumn} = '00000000-0000-0000-0000-000000000000'";
            await fillDguid.ExecuteNonQueryAsync();
        }

        await using (var syncRecords = connection.CreateCommand())
        {
            syncRecords.CommandText = $@"
INSERT INTO ApplicationRecords (ApplicationTableId, DGUID, CreatedAtUtc)
SELECT @tableId, t.{dguidColumn}, @created
FROM {tableIdentifier} t
WHERE t.{dguidColumn} IS NOT NULL
AND NOT EXISTS (
    SELECT 1 FROM ApplicationRecords ar
    WHERE ar.ApplicationTableId = @tableId AND ar.DGUID = t.{dguidColumn}
)";
            AddParameter(syncRecords, "@tableId", payload.TableId);
            AddParameter(syncRecords, "@created", DateTime.UtcNow);
            await syncRecords.ExecuteNonQueryAsync();
        }

        var refreshed = await LoadTableMetadataAsync(payload.TableId, context.OwnerUserId);
        return new JsonResult(new { success = true, message = "Columns synced.", columns = refreshed?.Columns ?? new List<ApplicationColumnRow>() });
    }

    public async Task<IActionResult> OnPostDatabaseSyncAsync()
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }
        if (!context.CanManage)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to sync applications." }) { StatusCode = 403 };
        }

        await EnsureMetadataTablesAsync();
        await EnsurePermissionsSystemTableAsync();

        var repaired = 0;
        var tables = await LoadTablesAsync(context.OwnerUserId);
        foreach (var table in tables.Where(t => !t.IsSystem))
        {
            await OnPostSyncColumnsAsync(new SyncColumnsPayload { TableId = table.Id });
            repaired++;
        }

        return new JsonResult(new { success = true, repaired, message = $"Database synced. {repaired} application table(s) checked." });
    }

    public async Task<IActionResult> OnGetPermissionsAsync([FromQuery] Guid tableId)
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
        var table = await LoadAnyTableByIdAsync(context.OwnerUserId, tableId);
        if (table == null)
        {
            return new JsonResult(new { success = false, message = "Table not found." }) { StatusCode = 404 };
        }

        if (!await CanManageTableAsync(context, tableId))
        {
            return new JsonResult(new { success = false, message = "You do not have permission to manage table permissions." }) { StatusCode = 403 };
        }

        var explicitPermissions = await ApplicationTablePermissionService.GetExplicitPermissionsForTableAsync(_db, context.OwnerUserId, tableId);
        var members = await LoadActivePermissionSubjectsAsync(context.OwnerUserId);
        var response = new List<object>
        {
            new
            {
                userId = context.OwnerUserId,
                displayName = "Owner",
                email = context.OwnerEmail,
                role = "Owner",
                effectiveAccessLevel = ApplicationTablePermissionService.AccessAdmin,
                explicitAccessLevel = ApplicationTablePermissionService.AccessAdmin,
                locked = true
            }
        };

        foreach (var member in members)
        {
            explicitPermissions.TryGetValue(member.UserId, out var explicitAccess);
            var effective = ApplicationTablePermissionService.ResolveEffectivePermission(
                member.UserId,
                context.OwnerUserId,
                member.Role,
                explicitAccess);

            response.Add(new
            {
                userId = member.UserId,
                displayName = member.DisplayName,
                email = member.Email,
                role = member.Role,
                effectiveAccessLevel = effective.EffectiveAccessLevel,
                explicitAccessLevel = explicitAccess ?? effective.EffectiveAccessLevel,
                locked = false
            });
        }

        var auditEntries = await LoadPermissionAuditAsync(context.OwnerUserId, tableId);

        return new JsonResult(new
        {
            success = true,
            tableId,
            tableName = table.DisplayName ?? table.TableName,
            members = response,
            auditEntries
        });
    }

    public async Task<IActionResult> OnPostSavePermissionsAsync([FromBody] SavePermissionsPayload payload)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        if (payload.TableId == Guid.Empty)
        {
            return new JsonResult(new { success = false, message = "Invalid table reference." }) { StatusCode = 400 };
        }

        await EnsureMetadataTablesAsync();
        var table = await LoadAnyTableByIdAsync(context.OwnerUserId, payload.TableId);
        if (table == null)
        {
            return new JsonResult(new { success = false, message = "Table not found." }) { StatusCode = 404 };
        }

        if (!await CanManageTableAsync(context, payload.TableId))
        {
            return new JsonResult(new { success = false, message = "You do not have permission to manage table permissions." }) { StatusCode = 403 };
        }

        var previousExplicit = await ApplicationTablePermissionService.GetExplicitPermissionsForTableAsync(_db, context.OwnerUserId, payload.TableId);
        var entries = payload.Entries ?? new List<PermissionEntryPayload>();
        var validSubjects = await LoadActivePermissionSubjectsAsync(context.OwnerUserId);
        var validSubjectSet = new HashSet<string>(validSubjects.Select(s => s.UserId), StringComparer.Ordinal);
        var subjectRoleByUser = validSubjects.ToDictionary(s => s.UserId, s => s.Role, StringComparer.Ordinal);
        var subjectNameByUser = validSubjects.ToDictionary(s => s.UserId, s => s.DisplayName, StringComparer.Ordinal);
        var writes = new List<ApplicationTablePermissionService.UserPermissionWrite>();
        var nextByUser = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var subjectUserId = entry.UserId?.Trim();
            if (string.IsNullOrWhiteSpace(subjectUserId))
            {
                continue;
            }

            if (string.Equals(subjectUserId, context.OwnerUserId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!validSubjectSet.Contains(subjectUserId))
            {
                return new JsonResult(new { success = false, message = "One or more users are invalid for this workspace." }) { StatusCode = 400 };
            }

            var level = ApplicationTablePermissionService.NormalizeAccess(entry.AccessLevel);
            writes.Add(new ApplicationTablePermissionService.UserPermissionWrite(subjectUserId, level));
            nextByUser[subjectUserId] = level;
        }

        await ApplicationTablePermissionService.UpsertPermissionsAsync(_db, context.OwnerUserId, payload.TableId, writes);
        await PermissionSetupOnboardingService.MarkCompletedAsync(_db, context.OwnerUserId);

        var auditWrites = new List<PermissionAuditWrite>();
        foreach (var kvp in nextByUser)
        {
            var subjectUserId = kvp.Key;
            var newExplicit = kvp.Value;
            previousExplicit.TryGetValue(subjectUserId, out var previousExplicitRaw);
            var previousExplicitLevel = string.IsNullOrWhiteSpace(previousExplicitRaw)
                ? null
                : ApplicationTablePermissionService.NormalizeAccess(previousExplicitRaw);

            var teamRole = subjectRoleByUser.TryGetValue(subjectUserId, out var roleValue) ? roleValue : "Viewer";
            var previousEffective = ApplicationTablePermissionService.ResolveEffectiveAccessLevel(
                subjectUserId,
                context.OwnerUserId,
                teamRole,
                previousExplicitLevel);
            var newEffective = ApplicationTablePermissionService.ResolveEffectiveAccessLevel(
                subjectUserId,
                context.OwnerUserId,
                teamRole,
                newExplicit);

            var explicitChanged = !string.Equals(previousExplicitLevel ?? string.Empty, newExplicit, StringComparison.OrdinalIgnoreCase);
            var effectiveChanged = !string.Equals(previousEffective, newEffective, StringComparison.OrdinalIgnoreCase);
            if (!explicitChanged && !effectiveChanged)
            {
                continue;
            }

            auditWrites.Add(new PermissionAuditWrite(
                SubjectUserId: subjectUserId,
                SubjectDisplayName: subjectNameByUser.TryGetValue(subjectUserId, out var displayName) ? displayName : subjectUserId,
                PreviousAccessLevel: previousEffective,
                NewAccessLevel: newEffective));
        }

        await WritePermissionAuditAsync(
            ownerUserId: context.OwnerUserId,
            tableId: payload.TableId,
            tableName: table.DisplayName ?? table.TableName,
            changedByUserId: context.User.Id,
            changedByDisplayName: context.User.GetFriendlyName(),
            changes: auditWrites);

        return new JsonResult(new
        {
            success = true,
            message = "Permissions saved.",
            changedCount = auditWrites.Count
        });
    }

    private static void AddParameter(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static (bool IsValid, string Message) ValidateColumn(ColumnPayload column)
    {
        var type = column.Type?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(type))
        {
            return (false, $"Column \"{column.Name}\" must have a type.");
        }

        return type.ToLowerInvariant() switch
        {
            "int" => (true, string.Empty),
            "bigint" => (true, string.Empty),
            "bit" => (true, string.Empty),
            "datetime" => (true, string.Empty),
            "float" => (true, string.Empty),
            "nvarchar" => ValidateLength(column, 1, 4000),
            "varchar" => ValidateLength(column, 1, 8000),
            "char" => ValidateLength(column, 1, 8000),
            "decimal" => ValidatePrecision(column),
            "numeric" => ValidatePrecision(column),
            "image" => (true, string.Empty),
            _ => (false, $"Column \"{column.Name}\" has an unsupported type.")
        };
    }

    private static (bool IsValid, string Message) ValidateLength(ColumnPayload column, int min, int max)
    {
        if (!column.Length.HasValue || column.Length.Value < min || column.Length.Value > max)
        {
            return (false, $"Column \"{column.Name}\" requires a length between {min} and {max}.");
        }
        return (true, string.Empty);
    }

    private static (bool IsValid, string Message) ValidatePrecision(ColumnPayload column)
    {
        if (!column.Precision.HasValue || column.Precision.Value < 1 || column.Precision.Value > 38)
        {
            return (false, $"Column \"{column.Name}\" requires a precision between 1 and 38.");
        }

        if (column.Scale.HasValue && column.Scale.Value > column.Precision.Value)
        {
            return (false, $"Column \"{column.Name}\" scale cannot exceed precision.");
        }

        return (true, string.Empty);
    }

    private string BuildCreateTableSql(string tableName, List<ColumnPayload> columns)
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        var dguid = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? "\"DGUID\" TEXT NOT NULL DEFAULT (lower(hex(randomblob(16))))"
            : "[DGUID] uniqueidentifier NOT NULL DEFAULT NEWID()";

        var columnSql = columns.Select(column =>
        {
            var name = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
                ? $"\"{column.Name}\""
                : $"[{column.Name}]";
            var type = column.Type.Trim().ToLowerInvariant() switch
            {
                "int" => "int",
                "bigint" => "bigint",
                "bit" => provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "integer" : "bit",
                "datetime" => provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "text" : "datetime2",
                "float" => "float",
                "nvarchar" => $"nvarchar({column.Length})",
                "varchar" => $"varchar({column.Length})",
                "char" => $"char({column.Length})",
                "decimal" => $"decimal({column.Precision},{column.Scale ?? 0})",
                "numeric" => $"numeric({column.Precision},{column.Scale ?? 0})",
                "image" => provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) ? "text" : "nvarchar(max)",
                _ => "nvarchar(255)"
            };
            var nullable = column.IsNullable ? "NULL" : "NOT NULL";
            return $"{name} {type} {nullable}";
        });

        var identifier = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)
            ? $"\"{tableName}\""
            : $"[{tableName}]";

        return $"CREATE TABLE {identifier} ({dguid}, {string.Join(", ", columnSql)})";
    }

    private string BuildAddColumnSql(string tableName, ColumnPayload column)
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var identifier = isSqlite ? $"\"{tableName}\"" : $"[{tableName}]";
        var name = isSqlite ? $"\"{column.Name}\"" : $"[{column.Name}]";
        var type = column.Type.Trim().ToLowerInvariant() switch
        {
            "int" => "int",
            "bigint" => "bigint",
            "bit" => isSqlite ? "integer" : "bit",
            "datetime" => isSqlite ? "text" : "datetime2",
            "float" => "float",
            "nvarchar" => $"nvarchar({column.Length})",
            "varchar" => $"varchar({column.Length})",
            "char" => $"char({column.Length})",
            "decimal" => $"decimal({column.Precision},{column.Scale ?? 0})",
            "numeric" => $"numeric({column.Precision},{column.Scale ?? 0})",
            "image" => isSqlite ? "text" : "nvarchar(max)",
            _ => "nvarchar(255)"
        };
        var nullable = column.IsNullable ? "NULL" : "NOT NULL";
        return $"ALTER TABLE {identifier} ADD {name} {type} {nullable}";
    }

    private async Task<bool> TableExistsAsync(string tableName)
    {
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        return await TableExistsAsync(connection, tableName);
    }

    private async Task<bool> TableExistsAsync(DbConnection connection, string tableName)
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
        }
        else
        {
            command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @name";
        }

        AddParameter(command, "@name", tableName);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
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
);
CREATE TABLE IF NOT EXISTS ApplicationPermissionAudits (
    Id TEXT PRIMARY KEY,
    OwnerUserId TEXT NOT NULL,
    ApplicationTableId TEXT NOT NULL,
    ApplicationTableName TEXT NOT NULL,
    SubjectUserId TEXT NOT NULL,
    SubjectDisplayName TEXT NULL,
    PreviousAccessLevel TEXT NOT NULL,
    NewAccessLevel TEXT NOT NULL,
    ChangedByUserId TEXT NOT NULL,
    ChangedByDisplayName TEXT NULL,
    ChangedAtUtc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_ApplicationPermissionAudits_Owner_Table_Changed
    ON ApplicationPermissionAudits (OwnerUserId, ApplicationTableId, ChangedAtUtc DESC);");
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
);
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ApplicationPermissionAudits' AND xtype='U')
CREATE TABLE ApplicationPermissionAudits (
    Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NOT NULL,
    ApplicationTableId UNIQUEIDENTIFIER NOT NULL,
    ApplicationTableName NVARCHAR(180) NOT NULL,
    SubjectUserId NVARCHAR(450) NOT NULL,
    SubjectDisplayName NVARCHAR(180) NULL,
    PreviousAccessLevel NVARCHAR(32) NOT NULL,
    NewAccessLevel NVARCHAR(32) NOT NULL,
    ChangedByUserId NVARCHAR(450) NOT NULL,
    ChangedByDisplayName NVARCHAR(180) NULL,
    ChangedAtUtc DATETIME2 NOT NULL
);
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_ApplicationPermissionAudits_Owner_Table_Changed'
      AND object_id = OBJECT_ID('ApplicationPermissionAudits')
)
CREATE INDEX IX_ApplicationPermissionAudits_Owner_Table_Changed
    ON ApplicationPermissionAudits (OwnerUserId, ApplicationTableId, ChangedAtUtc DESC);");
    }

    private async Task<List<object>> LoadPermissionAuditAsync(string ownerUserId, Guid tableId, int take = 14)
    {
        var rows = new List<object>();
        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = isSqlite
            ? "SELECT SubjectDisplayName, PreviousAccessLevel, NewAccessLevel, ChangedByDisplayName, ChangedAtUtc FROM ApplicationPermissionAudits WHERE OwnerUserId = @owner AND ApplicationTableId = @tableId ORDER BY datetime(ChangedAtUtc) DESC LIMIT @take"
            : "SELECT TOP (@take) SubjectDisplayName, PreviousAccessLevel, NewAccessLevel, ChangedByDisplayName, ChangedAtUtc FROM ApplicationPermissionAudits WHERE OwnerUserId = @owner AND ApplicationTableId = @tableId ORDER BY ChangedAtUtc DESC";
        AddParameter(command, "@owner", ownerUserId);
        AddParameter(command, "@tableId", tableId);
        AddParameter(command, "@take", take);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var changedAtUtc = isSqlite
                ? DateTime.Parse(reader.GetString(4))
                : reader.GetDateTime(4);
            rows.Add(new
            {
                subjectDisplayName = reader.IsDBNull(0) ? "User" : reader.GetString(0),
                previousAccessLevel = reader.IsDBNull(1) ? ApplicationTablePermissionService.AccessViewOnly : reader.GetString(1),
                newAccessLevel = reader.IsDBNull(2) ? ApplicationTablePermissionService.AccessViewOnly : reader.GetString(2),
                changedByDisplayName = reader.IsDBNull(3) ? "System" : reader.GetString(3),
                changedAtUtc
            });
        }

        return rows;
    }

    private async Task WritePermissionAuditAsync(
        string ownerUserId,
        Guid tableId,
        string tableName,
        string changedByUserId,
        string? changedByDisplayName,
        List<PermissionAuditWrite> changes)
    {
        if (changes.Count == 0)
        {
            return;
        }

        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        foreach (var change in changes)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO ApplicationPermissionAudits
(Id, OwnerUserId, ApplicationTableId, ApplicationTableName, SubjectUserId, SubjectDisplayName, PreviousAccessLevel, NewAccessLevel, ChangedByUserId, ChangedByDisplayName, ChangedAtUtc)
VALUES (@id, @owner, @tableId, @tableName, @subject, @subjectName, @prev, @next, @changedBy, @changedByName, @changedAt)";
            AddParameter(command, "@id", Guid.NewGuid());
            AddParameter(command, "@owner", ownerUserId);
            AddParameter(command, "@tableId", tableId);
            AddParameter(command, "@tableName", tableName);
            AddParameter(command, "@subject", change.SubjectUserId);
            AddParameter(command, "@subjectName", change.SubjectDisplayName);
            AddParameter(command, "@prev", change.PreviousAccessLevel);
            AddParameter(command, "@next", change.NewAccessLevel);
            AddParameter(command, "@changedBy", changedByUserId);
            AddParameter(command, "@changedByName", string.IsNullOrWhiteSpace(changedByDisplayName) ? "Administrator" : changedByDisplayName);
            AddParameter(command, "@changedAt", DateTime.UtcNow);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task<List<ApplicationTableRow>> LoadTablesAsync(string ownerUserId)
    {
        var tables = new Dictionary<Guid, ApplicationTableRow>();
        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, TableName, CreatedAtUtc FROM ApplicationTables WHERE OwnerUserId = @owner ORDER BY CreatedAtUtc DESC";
            AddParameter(command, "@owner", ownerUserId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = isSqlite ? Guid.Parse(reader.GetString(0)) : reader.GetGuid(0);
                tables[id] = new ApplicationTableRow
                {
                    Id = id,
                    TableName = reader.GetString(1),
                    CreatedAtUtc = isSqlite
                        ? DateTime.Parse(reader.GetString(2))
                        : reader.GetDateTime(2)
                };
            }
        }

        if (tables.Count == 0)
        {
            return new List<ApplicationTableRow>();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ApplicationTableId, ColumnName, DataType, Length, Precision, Scale, IsNullable FROM ApplicationTableColumns WHERE ApplicationTableId IN (" +
                                  string.Join(", ", tables.Keys.Select(k => $"'{k}'")) +
                                  ") ORDER BY ColumnName";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var tableId = isSqlite ? Guid.Parse(reader.GetString(0)) : reader.GetGuid(0);
                if (!tables.TryGetValue(tableId, out var table))
                {
                    continue;
                }

                table.Columns.Add(new ApplicationColumnRow
                {
                    Name = reader.GetString(1),
                    Type = reader.GetString(2),
                    Length = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    Precision = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    Scale = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    IsNullable = !reader.IsDBNull(6) && (isSqlite ? reader.GetInt32(6) == 1 : reader.GetBoolean(6))
                });
            }
        }

        return tables.Values.ToList();
    }

    private async Task<Dictionary<string, DateTime>> RemoveSystemApplicationMetadataAsync(string ownerUserId)
    {
        var createdAtMap = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var system in SystemTables)
        {
            Guid? tableId = null;
            DateTime? createdAt = null;
            await using (var find = connection.CreateCommand())
            {
                find.Transaction = transaction;
                find.CommandText = "SELECT Id, CreatedAtUtc FROM ApplicationTables WHERE OwnerUserId = @owner AND TableName = @name";
                AddParameter(find, "@owner", ownerUserId);
                AddParameter(find, "@name", system.TableName);
                await using var reader = await find.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var provider = _db.Database.ProviderName ?? string.Empty;
                    var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
                    tableId = isSqlite ? Guid.Parse(reader.GetString(0)) : reader.GetGuid(0);
                    createdAt = isSqlite ? DateTime.Parse(reader.GetString(1)) : reader.GetDateTime(1);
                }
            }

            if (!tableId.HasValue)
            {
                continue;
            }

            createdAtMap[system.TableName] = createdAt ?? DateTime.UtcNow;

            await using (var deleteRecords = connection.CreateCommand())
            {
                deleteRecords.Transaction = transaction;
                deleteRecords.CommandText = "DELETE FROM ApplicationRecords WHERE ApplicationTableId = @id";
                AddParameter(deleteRecords, "@id", tableId.Value);
                await deleteRecords.ExecuteNonQueryAsync();
            }
            await using (var deleteColumns = connection.CreateCommand())
            {
                deleteColumns.Transaction = transaction;
                deleteColumns.CommandText = "DELETE FROM ApplicationTableColumns WHERE ApplicationTableId = @id";
                AddParameter(deleteColumns, "@id", tableId.Value);
                await deleteColumns.ExecuteNonQueryAsync();
            }
            await using (var deleteTable = connection.CreateCommand())
            {
                deleteTable.Transaction = transaction;
                deleteTable.CommandText = "DELETE FROM ApplicationTables WHERE Id = @id";
                AddParameter(deleteTable, "@id", tableId.Value);
                await deleteTable.ExecuteNonQueryAsync();
            }
        }

        await transaction.CommitAsync();
        return createdAtMap;
    }

    private async Task<List<ApplicationTableRow>> LoadSystemTablesAsync(Dictionary<string, DateTime> createdAtMap)
    {
        var list = new List<ApplicationTableRow>();
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        foreach (var system in SystemTables)
        {
            var exists = await TableExistsAsync(connection, system.TableName);
            if (!exists && !string.Equals(system.TableName, "ReactBuilders", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var columns = new List<ApplicationColumnRow>();
            if (exists)
            {
                var schemaColumns = await LoadTableSchemaAsync(connection, system.TableName);
                columns = schemaColumns
                    .Where(c => !string.Equals(c.Name, "DGUID", StringComparison.OrdinalIgnoreCase))
                    .Select(c => new ApplicationColumnRow
                    {
                        Name = c.Name,
                        Type = c.Type,
                        Length = c.Length,
                        Precision = c.Precision,
                        Scale = c.Scale,
                        IsNullable = c.IsNullable
                    })
                    .ToList();
            }

            createdAtMap.TryGetValue(system.TableName, out var createdAt);
            list.Add(new ApplicationTableRow
            {
                Id = GetSystemTableStableId(system.TableName),
                TableName = system.TableName,
                DisplayName = system.DisplayName,
                CreatedAtUtc = createdAt == default ? DateTime.UtcNow : createdAt,
                Columns = columns,
                IsSystem = true
            });
        }

        return list;
    }

    private async Task<ApplicationTableRow?> LoadTableMetadataAsync(Guid tableId, string ownerUserId)
    {
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        await using var tableCmd = connection.CreateCommand();
        tableCmd.CommandText = "SELECT Id, TableName, CreatedAtUtc FROM ApplicationTables WHERE Id = @id AND OwnerUserId = @owner";
        AddParameter(tableCmd, "@id", tableId);
        AddParameter(tableCmd, "@owner", ownerUserId);
        await using var reader = await tableCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        var id = isSqlite ? Guid.Parse(reader.GetString(0)) : reader.GetGuid(0);
        var table = new ApplicationTableRow
        {
            Id = id,
            TableName = reader.GetString(1),
            CreatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(2)) : reader.GetDateTime(2)
        };
        await reader.CloseAsync();

        await using var columnCmd = connection.CreateCommand();
        columnCmd.CommandText = "SELECT ColumnName, DataType, Length, Precision, Scale, IsNullable FROM ApplicationTableColumns WHERE ApplicationTableId = @id ORDER BY ColumnName";
        AddParameter(columnCmd, "@id", id);
        await using var colReader = await columnCmd.ExecuteReaderAsync();
        while (await colReader.ReadAsync())
        {
            table.Columns.Add(new ApplicationColumnRow
            {
                Name = colReader.GetString(0),
                Type = colReader.GetString(1),
                Length = colReader.IsDBNull(2) ? null : colReader.GetInt32(2),
                Precision = colReader.IsDBNull(3) ? null : colReader.GetInt32(3),
                Scale = colReader.IsDBNull(4) ? null : colReader.GetInt32(4),
                IsNullable = !colReader.IsDBNull(5) && (isSqlite ? colReader.GetInt32(5) == 1 : colReader.GetBoolean(5))
            });
        }

        return table;
    }

    private static Guid GetSystemTableStableId(string tableName)
    {
        // Stable GUID so system table rows don't appear to change identity between page loads.
        var bytes = System.Text.Encoding.UTF8.GetBytes($"bugence-system-table:{tableName.ToLowerInvariant()}");
        var hash = MD5.HashData(bytes);
        return new Guid(hash);
    }

    private async Task<List<SchemaColumn>> LoadTableSchemaAsync(DbConnection connection, string tableName)
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var columns = new List<SchemaColumn>();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        if (isSqlite)
        {
            await using var pragma = connection.CreateCommand();
            pragma.CommandText = $"PRAGMA table_info(\"{tableName}\")";
            await ExecuteWithRetryAsync(async () =>
            {
                await using var reader = await pragma.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var name = reader.GetString(1);
                    var type = reader.IsDBNull(2) ? "nvarchar" : reader.GetString(2);
                    var notNull = reader.GetInt32(3) == 1;
                    var parsed = ParseSqliteType(type);
                    columns.Add(new SchemaColumn
                    {
                        Name = name,
                        Type = parsed.Type,
                        Length = parsed.Length,
                        Precision = parsed.Precision,
                        Scale = parsed.Scale,
                        IsNullable = !notNull
                    });
                }
            }, isSqlite);

            return columns;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @name";
        AddParameter(command, "@name", tableName);
        await ExecuteWithRetryAsync(async () =>
        {
            await using var sqlReader = await command.ExecuteReaderAsync();
            while (await sqlReader.ReadAsync())
            {
                var name = sqlReader.GetString(0);
                var type = sqlReader.GetString(1).ToLowerInvariant();
                var length = sqlReader.IsDBNull(2) ? (int?)null : sqlReader.GetInt32(2);
                var precision = sqlReader.IsDBNull(3) ? null : (int?)Convert.ToInt32(sqlReader.GetByte(3));
                var scale = sqlReader.IsDBNull(4) ? null : (int?)Convert.ToInt32(sqlReader.GetInt32(4));
                var nullable = !string.Equals(sqlReader.GetString(5), "NO", StringComparison.OrdinalIgnoreCase);

                var normalized = NormalizeSqlType(type);
                columns.Add(new SchemaColumn
                {
                    Name = name,
                    Type = normalized,
                    Length = length is -1 ? (int?)null : length,
                    Precision = precision,
                    Scale = scale,
                    IsNullable = nullable
                });
            }
        }, isSqlite);

        return columns;
    }

    private static async Task ExecuteWithRetryAsync(Func<Task> action, bool enableRetry)
    {
        if (!enableRetry)
        {
            await action();
            return;
        }

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex) when (attempt < maxAttempts && ex.SqliteErrorCode == 5)
            {
                await Task.Delay(80 * attempt);
            }
        }
    }

    private static (string Type, int? Length, int? Precision, int? Scale) ParseSqliteType(string raw)
    {
        var type = raw.ToLowerInvariant();
        int? length = null;
        int? precision = null;
        int? scale = null;

        var parenStart = type.IndexOf('(');
        if (parenStart >= 0 && type.EndsWith(")"))
        {
            var inner = type[(parenStart + 1)..^1];
            type = type[..parenStart];
            var parts = inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0 && int.TryParse(parts[0], out var p0))
            {
                length = p0;
                precision = p0;
            }
            if (parts.Length > 1 && int.TryParse(parts[1], out var p1))
            {
                scale = p1;
            }
        }

        if (type.Contains("bigint"))
        {
            return ("bigint", null, null, null);
        }
        if (type.Contains("int"))
        {
            return ("int", null, null, null);
        }
        if (type.Contains("decimal"))
        {
            return ("decimal", null, precision, scale);
        }
        if (type.Contains("numeric"))
        {
            return ("numeric", null, precision, scale);
        }
        if (type.Contains("varchar"))
        {
            return ("varchar", length, null, null);
        }
        if (type.Contains("char"))
        {
            return ("char", length, null, null);
        }
        if (type.Contains("datetime"))
        {
            return ("datetime", null, null, null);
        }
        if (type.Contains("float") || type.Contains("real"))
        {
            return ("float", null, null, null);
        }
        if (type.Contains("text"))
        {
            return ("nvarchar", null, null, null);
        }

        return ("nvarchar", length, null, null);
    }

    private static string NormalizeSqlType(string type)
    {
        return type switch
        {
            "datetime2" => "datetime",
            _ => type
        };
    }

    private async Task<AppAccessContext?> GetAccessContextAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return null;
        }
        var scope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        var ownerUser = scope.IsOwner
            ? user
            : await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == scope.OwnerUserId);

        return new AppAccessContext
        {
            User = user,
            OwnerUserId = scope.OwnerUserId,
            OwnerEmail = ownerUser?.Email ?? "—",
            CanManage = scope.CanManage,
            IsOwner = scope.IsOwner,
            TeamRole = scope.TeamRole
        };
    }

    private async Task<List<PermissionSubjectRow>> LoadActivePermissionSubjectsAsync(string ownerUserId)
    {
        return await _db.TeamMembers
            .AsNoTracking()
            .Where(m => m.OwnerUserId == ownerUserId && m.UserId != null && m.Status == "Active")
            .OrderBy(m => m.DisplayName)
            .Select(m => new PermissionSubjectRow
            {
                UserId = m.UserId!,
                DisplayName = m.DisplayName,
                Email = m.Email,
                Role = m.Role
            })
            .ToListAsync();
    }

    private async Task<bool> CanManageTableAsync(AppAccessContext context, Guid tableId)
    {
        var explicitAccess = await ApplicationTablePermissionService.GetExplicitPermissionAsync(
            _db,
            context.OwnerUserId,
            tableId,
            context.User.Id);

        var permission = ApplicationTablePermissionService.ResolveEffectivePermission(
            context.User.Id,
            context.OwnerUserId,
            context.TeamRole,
            explicitAccess);

        return permission.CanManage;
    }

    private async Task<ApplicationTableRow?> LoadAnyTableByIdAsync(string ownerUserId, Guid tableId)
    {
        var customTable = await LoadTableMetadataAsync(tableId, ownerUserId);
        if (customTable != null)
        {
            return customTable;
        }

        foreach (var system in SystemTables)
        {
            if (GetSystemTableStableId(system.TableName) != tableId)
            {
                continue;
            }

            if (!await TableExistsAsync(system.TableName))
            {
                return null;
            }

            return new ApplicationTableRow
            {
                Id = tableId,
                TableName = system.TableName,
                DisplayName = system.DisplayName,
                IsSystem = true
            };
        }

        return null;
    }

    private async Task EnsurePermissionsSystemTableAsync()
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

    private static string GetInitials(string name)
    {
        var initials = new string(name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]))
            .Take(2)
            .ToArray());
        return string.IsNullOrWhiteSpace(initials) ? "AD" : initials;
    }

    private sealed class AppAccessContext
    {
        public required ApplicationUser User { get; init; }
        public required string OwnerUserId { get; init; }
        public required string OwnerEmail { get; init; }
        public bool CanManage { get; init; }
        public bool IsOwner { get; init; }
        public string? TeamRole { get; init; }
    }

    public class ApplicationTableRow
    {
        public Guid Id { get; set; }
        public int DisplayId { get; set; }
        public string TableName { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public List<ApplicationColumnRow> Columns { get; set; } = new();
        public bool IsSystem { get; set; }
        public bool CanView { get; set; } = true;
        public bool CanManage { get; set; }
        public string EffectiveAccessLevel { get; set; } = ApplicationTablePermissionService.AccessViewOnly;
    }

    public class PermissionEntryPayload
    {
        public string? UserId { get; set; }
        public string? AccessLevel { get; set; }
    }

    public class SavePermissionsPayload
    {
        [Required]
        public Guid TableId { get; set; }

        public List<PermissionEntryPayload>? Entries { get; set; }
    }

    private class PermissionSubjectRow
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "Viewer";
    }

    private sealed record PermissionAuditWrite(
        string SubjectUserId,
        string SubjectDisplayName,
        string PreviousAccessLevel,
        string NewAccessLevel);

    public class ApplicationColumnRow
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int? Length { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public bool IsNullable { get; set; }
    }

    public class CreateTablePayload
    {
        [Required]
        public string? TableName { get; set; }

        public List<ColumnPayload>? Columns { get; set; }
    }

    public class UpdateTablePayload
    {
        [Required]
        public Guid TableId { get; set; }

        [Required]
        public string? TableName { get; set; }

        public List<ColumnPayload>? Columns { get; set; }
    }

    public class DeleteTablePayload
    {
        [Required]
        public Guid TableId { get; set; }
    }

    public class SyncColumnsPayload
    {
        [Required]
        public Guid TableId { get; set; }
    }

    private class SchemaColumn
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "nvarchar";
        public int? Length { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public bool IsNullable { get; set; }
    }

    public record ColumnPayload
    {
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public int? Length { get; init; }
        public int? Precision { get; init; }
        public int? Scale { get; init; }
        public bool IsNullable { get; init; }
        public string? OriginalName { get; init; }
        public bool IsDeleted { get; init; }
        public bool IsExisting { get; init; }
    }
}

