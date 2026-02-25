using System.Data.Common;
using BugenceEditConsole.Data;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public static class WorkflowSchemaService
{
    public static async Task EnsureLegacyColumnsAsync(ApplicationDbContext db)
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
            pragma.CommandText = "PRAGMA table_info(\"Workflows\")";
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
        AddIfMissing(existingColumns, alterStatements, "Caption", "ALTER TABLE \"Workflows\" ADD COLUMN \"Caption\" TEXT NULL");
        AddIfMissing(existingColumns, alterStatements, "WorkflowType", "ALTER TABLE \"Workflows\" ADD COLUMN \"WorkflowType\" TEXT NOT NULL DEFAULT 'Application Workflow'");
        AddIfMissing(existingColumns, alterStatements, "ApplicationId", "ALTER TABLE \"Workflows\" ADD COLUMN \"ApplicationId\" TEXT NULL");
        AddIfMissing(existingColumns, alterStatements, "FileListId", "ALTER TABLE \"Workflows\" ADD COLUMN \"FileListId\" INTEGER NOT NULL DEFAULT 0");
        AddIfMissing(existingColumns, alterStatements, "ViewOnApplication", "ALTER TABLE \"Workflows\" ADD COLUMN \"ViewOnApplication\" INTEGER NOT NULL DEFAULT 0");
        AddIfMissing(existingColumns, alterStatements, "StartupType", "ALTER TABLE \"Workflows\" ADD COLUMN \"StartupType\" INTEGER NOT NULL DEFAULT 1");
        AddIfMissing(existingColumns, alterStatements, "StartupArgument1", "ALTER TABLE \"Workflows\" ADD COLUMN \"StartupArgument1\" TEXT NULL");
        AddIfMissing(existingColumns, alterStatements, "StartupArgument2", "ALTER TABLE \"Workflows\" ADD COLUMN \"StartupArgument2\" TEXT NULL");
        AddIfMissing(existingColumns, alterStatements, "Diagram", "ALTER TABLE \"Workflows\" ADD COLUMN \"Diagram\" TEXT NULL");
        AddIfMissing(existingColumns, alterStatements, "KpiActivity", "ALTER TABLE \"Workflows\" ADD COLUMN \"KpiActivity\" TEXT NULL");
        AddIfMissing(existingColumns, alterStatements, "InActive", "ALTER TABLE \"Workflows\" ADD COLUMN \"InActive\" INTEGER NOT NULL DEFAULT 0");
        AddIfMissing(existingColumns, alterStatements, "AttachmentPath", "ALTER TABLE \"Workflows\" ADD COLUMN \"AttachmentPath\" TEXT NULL");
        AddIfMissing(existingColumns, alterStatements, "TriggerConfigJson", "ALTER TABLE \"Workflows\" ADD COLUMN \"TriggerConfigJson\" TEXT NULL");
        AddIfMissing(existingColumns, alterStatements, "DefinitionJson", "ALTER TABLE \"Workflows\" ADD COLUMN \"DefinitionJson\" TEXT NOT NULL DEFAULT '{}'");
        AddIfMissing(existingColumns, alterStatements, "CompanyId", "ALTER TABLE \"Workflows\" ADD COLUMN \"CompanyId\" TEXT NULL");

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
                // Safe to ignore when two requests race to add the same missing column.
            }
        }

        await using var backfill = connection.CreateCommand();
        backfill.CommandText = @"
UPDATE ""Workflows""
SET ""Caption"" = COALESCE(NULLIF(""Caption"", ''), ""Name""),
    ""WorkflowType"" = COALESCE(NULLIF(""WorkflowType"", ''), 'Application Workflow')
WHERE ""Caption"" IS NULL OR ""Caption"" = '' OR ""WorkflowType"" IS NULL OR ""WorkflowType"" = '';";
        await backfill.ExecuteNonQueryAsync();
    }

    private static async Task EnsureSqlServerColumnsAsync(ApplicationDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('Workflows','Caption') IS NULL ALTER TABLE [Workflows] ADD [Caption] NVARCHAR(180) NULL;
IF COL_LENGTH('Workflows','WorkflowType') IS NULL ALTER TABLE [Workflows] ADD [WorkflowType] NVARCHAR(80) NOT NULL CONSTRAINT DF_Workflows_WorkflowType DEFAULT('Application Workflow');
IF COL_LENGTH('Workflows','ApplicationId') IS NULL ALTER TABLE [Workflows] ADD [ApplicationId] NVARCHAR(180) NULL;
IF COL_LENGTH('Workflows','FileListId') IS NULL ALTER TABLE [Workflows] ADD [FileListId] INT NOT NULL CONSTRAINT DF_Workflows_FileListId DEFAULT(0);
IF COL_LENGTH('Workflows','ViewOnApplication') IS NULL ALTER TABLE [Workflows] ADD [ViewOnApplication] INT NOT NULL CONSTRAINT DF_Workflows_ViewOnApplication DEFAULT(0);
IF COL_LENGTH('Workflows','StartupType') IS NULL ALTER TABLE [Workflows] ADD [StartupType] INT NOT NULL CONSTRAINT DF_Workflows_StartupType DEFAULT(1);
IF COL_LENGTH('Workflows','StartupArgument1') IS NULL ALTER TABLE [Workflows] ADD [StartupArgument1] NVARCHAR(280) NULL;
IF COL_LENGTH('Workflows','StartupArgument2') IS NULL ALTER TABLE [Workflows] ADD [StartupArgument2] NVARCHAR(280) NULL;
IF COL_LENGTH('Workflows','Diagram') IS NULL ALTER TABLE [Workflows] ADD [Diagram] NVARCHAR(512) NULL;
IF COL_LENGTH('Workflows','KpiActivity') IS NULL ALTER TABLE [Workflows] ADD [KpiActivity] NVARCHAR(180) NULL;
IF COL_LENGTH('Workflows','InActive') IS NULL ALTER TABLE [Workflows] ADD [InActive] BIT NOT NULL CONSTRAINT DF_Workflows_InActive DEFAULT(0);
IF COL_LENGTH('Workflows','AttachmentPath') IS NULL ALTER TABLE [Workflows] ADD [AttachmentPath] NVARCHAR(512) NULL;
IF COL_LENGTH('Workflows','TriggerConfigJson') IS NULL ALTER TABLE [Workflows] ADD [TriggerConfigJson] NVARCHAR(MAX) NULL;
IF COL_LENGTH('Workflows','DefinitionJson') IS NULL ALTER TABLE [Workflows] ADD [DefinitionJson] NVARCHAR(MAX) NOT NULL CONSTRAINT DF_Workflows_DefinitionJson DEFAULT('{}');
IF COL_LENGTH('Workflows','CompanyId') IS NULL ALTER TABLE [Workflows] ADD [CompanyId] UNIQUEIDENTIFIER NULL;

UPDATE [Workflows]
SET [Caption] = COALESCE(NULLIF([Caption], ''), [Name]),
    [WorkflowType] = COALESCE(NULLIF([WorkflowType], ''), 'Application Workflow')
WHERE [Caption] IS NULL OR [Caption] = '' OR [WorkflowType] IS NULL OR [WorkflowType] = '';");
    }

    private static void AddIfMissing(HashSet<string> existing, List<string> alterStatements, string columnName, string sql)
    {
        if (!existing.Contains(columnName))
        {
            alterStatements.Add(sql);
        }
    }
}
