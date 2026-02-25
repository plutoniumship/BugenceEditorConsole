using System.Data;
using System.Data.Common;
using BugenceEditConsole.Data;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public static class ToolDataSyncService
{
    private static readonly (string TableName, string DisplayName)[] ToolTables =
    {
        ("DatabaseQuerySelectors", "Database Query Selector"),
        ("Masterpages", "Masterpage"),
        ("Pages", "Pages"),
        ("Permissions", "Permissions"),
        ("PagePortlets", "Portlets"),
        ("SystemProperties", "System Properties"),
        ("TempleteViewers", "Templete Viewer")
    };

    public static async Task SyncAllToolTablesAsync(ApplicationDbContext db, string ownerUserId)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
        {
            return;
        }

        await EnsureMetadataTablesAsync(db);
        foreach (var toolTable in ToolTables)
        {
            await SyncTableForOwnerAsync(db, ownerUserId, toolTable.TableName, toolTable.DisplayName);
        }
    }

    public static async Task SyncTableForOwnerAsync(ApplicationDbContext db, string ownerUserId, string tableName, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId) || string.IsNullOrWhiteSpace(tableName))
        {
            return;
        }

        await using var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        if (!await TableExistsAsync(connection, isSqlite, tableName))
        {
            return;
        }

        var tableId = await GetOrCreateApplicationTableAsync(connection, isSqlite, ownerUserId, tableName);
        var columns = await LoadTableSchemaAsync(connection, isSqlite, tableName);
        await SyncColumnsMetadataAsync(connection, isSqlite, tableId, columns);

        var hasDguid = columns.Any(c => string.Equals(c.Name, "DGUID", StringComparison.OrdinalIgnoreCase));
        if (!hasDguid)
        {
            return;
        }

        var tableIdentifier = Quote(tableName, isSqlite);
        var dguidColumn = Quote("DGUID", isSqlite);
        await using (var fillDguid = connection.CreateCommand())
        {
            fillDguid.CommandText = isSqlite
                ? $"UPDATE {tableIdentifier} SET {dguidColumn} = upper(hex(randomblob(16))) WHERE {dguidColumn} IS NULL OR {dguidColumn} = ''"
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
            AddParameter(syncRecords, "@tableId", tableId);
            AddParameter(syncRecords, "@created", DateTime.UtcNow);
            await syncRecords.ExecuteNonQueryAsync();
        }

        await using (var removeOrphans = connection.CreateCommand())
        {
            removeOrphans.CommandText = isSqlite
                ? $@"
DELETE FROM ApplicationRecords
WHERE ApplicationTableId = @tableId
  AND NOT EXISTS (
      SELECT 1 FROM {tableIdentifier} t
      WHERE lower(CAST(t.{dguidColumn} AS TEXT)) = lower(CAST(ApplicationRecords.DGUID AS TEXT))
  )"
                : $@"
DELETE FROM ApplicationRecords
WHERE ApplicationTableId = @tableId
  AND NOT EXISTS (
      SELECT 1 FROM {tableIdentifier} t
      WHERE lower(CONVERT(nvarchar(64), t.{dguidColumn})) = lower(CONVERT(nvarchar(64), ApplicationRecords.DGUID))
  )";
            AddParameter(removeOrphans, "@tableId", tableId);
            await removeOrphans.ExecuteNonQueryAsync();
        }
    }

    private static async Task EnsureMetadataTablesAsync(ApplicationDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync(@"
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

        await db.Database.ExecuteSqlRawAsync(@"
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

    private static async Task<Guid> GetOrCreateApplicationTableAsync(DbConnection connection, bool isSqlite, string ownerUserId, string tableName)
    {
        await using (var find = connection.CreateCommand())
        {
            find.CommandText = "SELECT Id FROM ApplicationTables WHERE OwnerUserId = @owner AND TableName = @name";
            AddParameter(find, "@owner", ownerUserId);
            AddParameter(find, "@name", tableName);
            var found = await find.ExecuteScalarAsync();
            if (found != null && found is not DBNull)
            {
                return isSqlite ? Guid.Parse(Convert.ToString(found)!) : (Guid)found;
            }
        }

        var createdId = Guid.NewGuid();
        await using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO ApplicationTables (Id, OwnerUserId, TableName, CreatedAtUtc) VALUES (@id, @owner, @name, @created)";
        AddParameter(insert, "@id", createdId);
        AddParameter(insert, "@owner", ownerUserId);
        AddParameter(insert, "@name", tableName);
        AddParameter(insert, "@created", DateTime.UtcNow);
        await insert.ExecuteNonQueryAsync();
        return createdId;
    }

    private static async Task<List<ColumnSchema>> LoadTableSchemaAsync(DbConnection connection, bool isSqlite, string tableName)
    {
        var columns = new List<ColumnSchema>();
        await using var command = connection.CreateCommand();
        if (isSqlite)
        {
            command.CommandText = $"PRAGMA table_info(\"{tableName}\")";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var rawType = reader.IsDBNull(2) ? "nvarchar" : reader.GetString(2);
                var (normalizedType, length, precision, scale) = NormalizeSqlType(rawType);
                columns.Add(new ColumnSchema(
                    Name: reader.GetString(1),
                    DataType: normalizedType,
                    Length: length,
                    Precision: precision,
                    Scale: scale,
                    IsNullable: reader.GetInt32(3) == 0));
            }
            return columns;
        }

        command.CommandText = @"
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = @table
ORDER BY ORDINAL_POSITION";
        AddParameter(command, "@table", tableName);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var dataType = reader.GetString(1);
                columns.Add(new ColumnSchema(
                    Name: reader.GetString(0),
                    DataType: dataType,
                    Length: reader.IsDBNull(2) ? null : Convert.ToInt32(reader.GetValue(2)),
                    Precision: reader.IsDBNull(3) ? null : Convert.ToInt32(reader.GetValue(3)),
                    Scale: reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4)),
                    IsNullable: string.Equals(reader.GetString(5), "YES", StringComparison.OrdinalIgnoreCase)));
            }
        }

        return columns;
    }

    private static async Task SyncColumnsMetadataAsync(DbConnection connection, bool isSqlite, Guid tableId, IReadOnlyCollection<ColumnSchema> schema)
    {
        var filtered = schema
            .Where(c => !string.Equals(c.Name, "DGUID", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var column in filtered)
        {
            await using var update = connection.CreateCommand();
            update.CommandText = @"
UPDATE ApplicationTableColumns
SET DataType = @type, Length = @length, Precision = @precision, Scale = @scale, IsNullable = @nullable
WHERE ApplicationTableId = @tableId AND ColumnName = @name";
            AddParameter(update, "@type", column.DataType);
            AddParameter(update, "@length", column.Length ?? (object)DBNull.Value);
            AddParameter(update, "@precision", column.Precision ?? (object)DBNull.Value);
            AddParameter(update, "@scale", column.Scale ?? (object)DBNull.Value);
            AddParameter(update, "@nullable", column.IsNullable);
            AddParameter(update, "@tableId", tableId);
            AddParameter(update, "@name", column.Name);
            var affected = await update.ExecuteNonQueryAsync();
            if (affected > 0)
            {
                continue;
            }

            await using var insert = connection.CreateCommand();
            insert.CommandText = @"
INSERT INTO ApplicationTableColumns (Id, ApplicationTableId, ColumnName, DataType, Length, Precision, Scale, IsNullable, CreatedAtUtc)
VALUES (@id, @tableId, @name, @type, @length, @precision, @scale, @nullable, @created)";
            AddParameter(insert, "@id", Guid.NewGuid());
            AddParameter(insert, "@tableId", tableId);
            AddParameter(insert, "@name", column.Name);
            AddParameter(insert, "@type", column.DataType);
            AddParameter(insert, "@length", column.Length ?? (object)DBNull.Value);
            AddParameter(insert, "@precision", column.Precision ?? (object)DBNull.Value);
            AddParameter(insert, "@scale", column.Scale ?? (object)DBNull.Value);
            AddParameter(insert, "@nullable", column.IsNullable);
            AddParameter(insert, "@created", DateTime.UtcNow);
            await insert.ExecuteNonQueryAsync();
        }

        await using var deleteRemoved = connection.CreateCommand();
        var names = filtered.Select(c => c.Name).ToList();
        if (names.Count == 0)
        {
            deleteRemoved.CommandText = "DELETE FROM ApplicationTableColumns WHERE ApplicationTableId = @tableId";
            AddParameter(deleteRemoved, "@tableId", tableId);
            await deleteRemoved.ExecuteNonQueryAsync();
            return;
        }

        var paramNames = new List<string>();
        for (var i = 0; i < names.Count; i++)
        {
            var p = $"@n{i}";
            paramNames.Add(p);
            AddParameter(deleteRemoved, p, names[i]);
        }
        deleteRemoved.CommandText = $"DELETE FROM ApplicationTableColumns WHERE ApplicationTableId = @tableId AND ColumnName NOT IN ({string.Join(", ", paramNames)})";
        AddParameter(deleteRemoved, "@tableId", tableId);
        await deleteRemoved.ExecuteNonQueryAsync();
    }

    private static async Task<bool> TableExistsAsync(DbConnection connection, bool isSqlite, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = isSqlite
            ? "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name"
            : "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME=@name";
        AddParameter(command, "@name", tableName);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    private static (string DataType, int? Length, int? Precision, int? Scale) NormalizeSqlType(string rawType)
    {
        var type = (rawType ?? string.Empty).Trim().ToLowerInvariant();
        int? length = null;
        int? precision = null;
        int? scale = null;

        var open = type.IndexOf('(');
        if (open >= 0 && type.EndsWith(")"))
        {
            var baseType = type[..open];
            var parts = type[(open + 1)..^1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0 && int.TryParse(parts[0], out var parsed))
            {
                length = parsed;
                precision = parsed;
            }
            if (parts.Length > 1 && int.TryParse(parts[1], out var parsedScale))
            {
                scale = parsedScale;
            }
            type = baseType;
        }

        if (type.Contains("bigint")) return ("bigint", null, null, null);
        if (type.Contains("int")) return ("int", null, null, null);
        if (type.Contains("decimal")) return ("decimal", null, precision, scale);
        if (type.Contains("numeric")) return ("numeric", null, precision, scale);
        if (type.Contains("nvarchar")) return ("nvarchar", length, null, null);
        if (type.Contains("varchar")) return ("varchar", length, null, null);
        if (type.Contains("char")) return ("char", length, null, null);
        if (type.Contains("datetime")) return ("datetime", null, null, null);
        if (type.Contains("real") || type.Contains("float")) return ("float", null, null, null);
        if (type.Contains("text")) return ("nvarchar", null, null, null);
        return ("nvarchar", length, null, null);
    }

    private static string Quote(string identifier, bool isSqlite) => isSqlite ? $"\"{identifier}\"" : $"[{identifier}]";

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private sealed record ColumnSchema(
        string Name,
        string DataType,
        int? Length,
        int? Precision,
        int? Scale,
        bool IsNullable);
}
