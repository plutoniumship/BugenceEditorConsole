using System.Data.Common;
using BugenceEditConsole.Data;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public static class DynamicVeSchemaService
{
    public static async Task EnsureActionBindingColumnsAsync(ApplicationDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        using var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        if (isSqlite)
        {
            await EnsureSqliteColumnsAsync(connection);
            return;
        }

        await EnsureSqlServerColumnsAsync(db);
    }

    private static async Task EnsureSqliteColumnsAsync(DbConnection connection)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA table_info(\"DynamicVeActionBindings\")";
            await using var reader = await pragma.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(1))
                {
                    existingColumns.Add(reader.GetString(1));
                }
            }
        }

        var alterStatements = new List<string>();
        AddIfMissing(existingColumns, alterStatements, "WorkflowDguid", "ALTER TABLE \"DynamicVeActionBindings\" ADD COLUMN \"WorkflowDguid\" TEXT NULL");
        AddIfMissing(existingColumns, alterStatements, "WorkflowNameSnapshot", "ALTER TABLE \"DynamicVeActionBindings\" ADD COLUMN \"WorkflowNameSnapshot\" TEXT NULL");
        AddIfMissing(existingColumns, alterStatements, "TriggerEvent", "ALTER TABLE \"DynamicVeActionBindings\" ADD COLUMN \"TriggerEvent\" TEXT NULL");
        AddIfMissing(existingColumns, alterStatements, "ValidationJson", "ALTER TABLE \"DynamicVeActionBindings\" ADD COLUMN \"ValidationJson\" TEXT NULL");

        foreach (var sql in alterStatements)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = sql;
            try
            {
                await alter.ExecuteNonQueryAsync();
            }
            catch (Exception ex) when (ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
                // Safe to ignore when parallel requests race to add the same missing column.
            }
        }

        // Backfill from linked workflow where available.
        await using var backfill = connection.CreateCommand();
        backfill.CommandText = @"
UPDATE ""DynamicVeActionBindings""
SET
  ""WorkflowDguid"" = (
    SELECT lower(replace(""Dguid"", '-', ''))
    FROM ""Workflows"" w
    WHERE w.""Id"" = ""DynamicVeActionBindings"".""WorkflowId""
  ),
  ""WorkflowNameSnapshot"" = (
    SELECT COALESCE(NULLIF(w.""Caption"", ''), w.""Name"")
    FROM ""Workflows"" w
    WHERE w.""Id"" = ""DynamicVeActionBindings"".""WorkflowId""
  ),
  ""TriggerEvent"" = COALESCE(NULLIF(""TriggerEvent"", ''), 'auto'),
  ""ValidationJson"" = COALESCE(NULLIF(""ValidationJson"", ''), '{}')
WHERE
  ""WorkflowId"" IS NOT NULL
  OR ""TriggerEvent"" IS NULL
  OR ""ValidationJson"" IS NULL;";
        await backfill.ExecuteNonQueryAsync();
    }

    private static async Task EnsureSqlServerColumnsAsync(ApplicationDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('DynamicVeActionBindings','WorkflowDguid') IS NULL ALTER TABLE [DynamicVeActionBindings] ADD [WorkflowDguid] NVARCHAR(64) NULL;
IF COL_LENGTH('DynamicVeActionBindings','WorkflowNameSnapshot') IS NULL ALTER TABLE [DynamicVeActionBindings] ADD [WorkflowNameSnapshot] NVARCHAR(256) NULL;
IF COL_LENGTH('DynamicVeActionBindings','TriggerEvent') IS NULL ALTER TABLE [DynamicVeActionBindings] ADD [TriggerEvent] NVARCHAR(24) NULL;
IF COL_LENGTH('DynamicVeActionBindings','ValidationJson') IS NULL ALTER TABLE [DynamicVeActionBindings] ADD [ValidationJson] NVARCHAR(MAX) NULL;

UPDATE b
SET
  b.[WorkflowDguid] = LOWER(REPLACE(w.[Dguid], '-', '')),
  b.[WorkflowNameSnapshot] = COALESCE(NULLIF(w.[Caption], ''), w.[Name]),
  b.[TriggerEvent] = COALESCE(NULLIF(b.[TriggerEvent], ''), 'auto'),
  b.[ValidationJson] = COALESCE(NULLIF(b.[ValidationJson], ''), '{}')
FROM [DynamicVeActionBindings] b
LEFT JOIN [Workflows] w ON w.[Id] = b.[WorkflowId]
WHERE b.[WorkflowId] IS NOT NULL OR b.[TriggerEvent] IS NULL OR b.[ValidationJson] IS NULL;");
    }

    private static void AddIfMissing(HashSet<string> existing, List<string> alterStatements, string columnName, string sql)
    {
        if (!existing.Contains(columnName))
        {
            alterStatements.Add(sql);
        }
    }
}
