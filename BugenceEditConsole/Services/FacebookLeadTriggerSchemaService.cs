using System.Data.Common;
using BugenceEditConsole.Data;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public static class FacebookLeadTriggerSchemaService
{
    public static async Task EnsureSchemaAsync(ApplicationDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        if (isSqlite)
        {
            using var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await EnsureSqliteTablesAsync(connection);
            return;
        }

        await EnsureSqlServerTablesAsync(db);
    }

    private static async Task EnsureSqliteTablesAsync(DbConnection connection)
    {
        var sql = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS "IntegrationConnections"(
                "Id" TEXT NOT NULL PRIMARY KEY,
                "WorkspaceId" TEXT NULL,
                "Provider" TEXT NOT NULL,
                "DisplayName" TEXT NOT NULL,
                "ExternalAccountId" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "ScopesJson" TEXT NULL,
                "AccessTokenEncrypted" TEXT NULL,
                "RefreshTokenEncrypted" TEXT NULL,
                "ExpiresAtUtc" TEXT NULL,
                "MetadataJson" TEXT NULL,
                "OwnerUserId" TEXT NOT NULL,
                "CompanyId" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_IntegrationConnections_OwnerUserId_Provider_ExternalAccountId"
            ON "IntegrationConnections"("OwnerUserId","Provider","ExternalAccountId");
            """,
            """
            CREATE TABLE IF NOT EXISTS "FacebookIntegrationAssetCaches"(
                "Id" TEXT NOT NULL PRIMARY KEY,
                "ConnectionId" TEXT NOT NULL,
                "AssetType" TEXT NOT NULL,
                "ExternalId" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "ParentExternalId" TEXT NULL,
                "RawJson" TEXT NULL,
                "FetchedAtUtc" TEXT NOT NULL
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FacebookIntegrationAssetCaches_ConnectionId_AssetType_ExternalId"
            ON "FacebookIntegrationAssetCaches"("ConnectionId","AssetType","ExternalId");
            """,
            """
            CREATE TABLE IF NOT EXISTS "WorkflowTriggerConfigs"(
                "Id" TEXT NOT NULL PRIMARY KEY,
                "WorkflowId" TEXT NOT NULL,
                "ActionNodeId" TEXT NOT NULL,
                "TriggerType" TEXT NOT NULL,
                "Mode" TEXT NOT NULL,
                "ConnectionId" TEXT NULL,
                "AdAccountId" TEXT NULL,
                "PageId" TEXT NULL,
                "FormId" TEXT NULL,
                "TriggerEvent" TEXT NOT NULL,
                "RequireConsent" INTEGER NOT NULL DEFAULT 0,
                "ReplayWindowMinutes" INTEGER NOT NULL DEFAULT 10,
                "MappingMode" TEXT NOT NULL,
                "MappingJson" TEXT NOT NULL DEFAULT '[]',
                "ValidationConfigJson" TEXT NOT NULL DEFAULT '{}',
                "OutputRoutingConfigJson" TEXT NOT NULL DEFAULT '{}',
                "OwnerUserId" TEXT NOT NULL,
                "CompanyId" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_WorkflowTriggerConfigs_WorkflowId_ActionNodeId_TriggerType"
            ON "WorkflowTriggerConfigs"("WorkflowId","ActionNodeId","TriggerType");
            """,
            """
            CREATE TABLE IF NOT EXISTS "WorkflowFieldMappingPresets"(
                "Id" TEXT NOT NULL PRIMARY KEY,
                "WorkspaceId" TEXT NULL,
                "Name" TEXT NOT NULL,
                "TriggerType" TEXT NOT NULL,
                "TargetEntity" TEXT NOT NULL,
                "MappingJson" TEXT NOT NULL DEFAULT '[]',
                "IsDefault" INTEGER NOT NULL DEFAULT 0,
                "OwnerUserId" TEXT NOT NULL,
                "CompanyId" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "WorkflowLeadDedupeStates"(
                "Id" TEXT NOT NULL PRIMARY KEY,
                "WorkflowId" TEXT NOT NULL,
                "ActionNodeId" TEXT NOT NULL,
                "DedupeKeyType" TEXT NOT NULL,
                "DedupeKeyValueHash" TEXT NOT NULL,
                "LastLeadId" TEXT NULL,
                "LastEventId" TEXT NULL,
                "LastProcessedAtUtc" TEXT NOT NULL
            );
            """,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_WorkflowLeadDedupeStates_Composite"
            ON "WorkflowLeadDedupeStates"("WorkflowId","ActionNodeId","DedupeKeyType","DedupeKeyValueHash");
            """,
            """
            CREATE TABLE IF NOT EXISTS "WorkflowTriggerEventLogs"(
                "Id" TEXT NOT NULL PRIMARY KEY,
                "WorkflowId" TEXT NOT NULL,
                "ActionNodeId" TEXT NOT NULL,
                "Provider" TEXT NOT NULL,
                "ExternalEventId" TEXT NULL,
                "LeadId" TEXT NULL,
                "Mode" TEXT NOT NULL,
                "Outcome" TEXT NOT NULL,
                "Reason" TEXT NULL,
                "PayloadJson" TEXT NULL,
                "ProcessedAtUtc" TEXT NOT NULL
            );
            """,
            """
            CREATE INDEX IF NOT EXISTS "IX_WorkflowTriggerEventLogs_WorkflowId_ActionNodeId_ProcessedAtUtc"
            ON "WorkflowTriggerEventLogs"("WorkflowId","ActionNodeId","ProcessedAtUtc");
            """
        };

        foreach (var statement in sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task EnsureSqlServerTablesAsync(ApplicationDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
IF OBJECT_ID('dbo.IntegrationConnections','U') IS NULL
BEGIN
    CREATE TABLE [dbo].[IntegrationConnections](
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        [WorkspaceId] UNIQUEIDENTIFIER NULL,
        [Provider] NVARCHAR(64) NOT NULL,
        [DisplayName] NVARCHAR(180) NOT NULL,
        [ExternalAccountId] NVARCHAR(180) NOT NULL,
        [Status] NVARCHAR(32) NOT NULL,
        [ScopesJson] NVARCHAR(MAX) NULL,
        [AccessTokenEncrypted] NVARCHAR(MAX) NULL,
        [RefreshTokenEncrypted] NVARCHAR(MAX) NULL,
        [ExpiresAtUtc] DATETIME2 NULL,
        [MetadataJson] NVARCHAR(MAX) NULL,
        [OwnerUserId] NVARCHAR(450) NOT NULL,
        [CompanyId] UNIQUEIDENTIFIER NULL,
        [CreatedAtUtc] DATETIME2 NOT NULL,
        [UpdatedAtUtc] DATETIME2 NOT NULL
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_IntegrationConnections_OwnerUserId_Provider_ExternalAccountId' AND object_id = OBJECT_ID('dbo.IntegrationConnections'))
    CREATE UNIQUE INDEX [IX_IntegrationConnections_OwnerUserId_Provider_ExternalAccountId] ON [dbo].[IntegrationConnections]([OwnerUserId],[Provider],[ExternalAccountId]);

IF OBJECT_ID('dbo.FacebookIntegrationAssetCaches','U') IS NULL
BEGIN
    CREATE TABLE [dbo].[FacebookIntegrationAssetCaches](
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        [ConnectionId] UNIQUEIDENTIFIER NOT NULL,
        [AssetType] NVARCHAR(32) NOT NULL,
        [ExternalId] NVARCHAR(180) NOT NULL,
        [Name] NVARCHAR(280) NOT NULL,
        [ParentExternalId] NVARCHAR(180) NULL,
        [RawJson] NVARCHAR(MAX) NULL,
        [FetchedAtUtc] DATETIME2 NOT NULL
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_FacebookIntegrationAssetCaches_ConnectionId_AssetType_ExternalId' AND object_id = OBJECT_ID('dbo.FacebookIntegrationAssetCaches'))
    CREATE UNIQUE INDEX [IX_FacebookIntegrationAssetCaches_ConnectionId_AssetType_ExternalId] ON [dbo].[FacebookIntegrationAssetCaches]([ConnectionId],[AssetType],[ExternalId]);

IF OBJECT_ID('dbo.WorkflowTriggerConfigs','U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkflowTriggerConfigs](
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        [WorkflowId] UNIQUEIDENTIFIER NOT NULL,
        [ActionNodeId] NVARCHAR(120) NOT NULL,
        [TriggerType] NVARCHAR(64) NOT NULL,
        [Mode] NVARCHAR(24) NOT NULL,
        [ConnectionId] UNIQUEIDENTIFIER NULL,
        [AdAccountId] NVARCHAR(64) NULL,
        [PageId] NVARCHAR(64) NULL,
        [FormId] NVARCHAR(64) NULL,
        [TriggerEvent] NVARCHAR(64) NOT NULL,
        [RequireConsent] BIT NOT NULL DEFAULT 0,
        [ReplayWindowMinutes] INT NOT NULL DEFAULT 10,
        [MappingMode] NVARCHAR(64) NOT NULL,
        [MappingJson] NVARCHAR(MAX) NOT NULL,
        [ValidationConfigJson] NVARCHAR(MAX) NOT NULL,
        [OutputRoutingConfigJson] NVARCHAR(MAX) NOT NULL,
        [OwnerUserId] NVARCHAR(450) NOT NULL,
        [CompanyId] UNIQUEIDENTIFIER NULL,
        [CreatedAtUtc] DATETIME2 NOT NULL,
        [UpdatedAtUtc] DATETIME2 NOT NULL
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkflowTriggerConfigs_WorkflowId_ActionNodeId_TriggerType' AND object_id = OBJECT_ID('dbo.WorkflowTriggerConfigs'))
    CREATE UNIQUE INDEX [IX_WorkflowTriggerConfigs_WorkflowId_ActionNodeId_TriggerType] ON [dbo].[WorkflowTriggerConfigs]([WorkflowId],[ActionNodeId],[TriggerType]);

IF OBJECT_ID('dbo.WorkflowFieldMappingPresets','U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkflowFieldMappingPresets](
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        [WorkspaceId] UNIQUEIDENTIFIER NULL,
        [Name] NVARCHAR(140) NOT NULL,
        [TriggerType] NVARCHAR(64) NOT NULL,
        [TargetEntity] NVARCHAR(32) NOT NULL,
        [MappingJson] NVARCHAR(MAX) NOT NULL,
        [IsDefault] BIT NOT NULL DEFAULT 0,
        [OwnerUserId] NVARCHAR(450) NOT NULL,
        [CompanyId] UNIQUEIDENTIFIER NULL,
        [CreatedAtUtc] DATETIME2 NOT NULL,
        [UpdatedAtUtc] DATETIME2 NOT NULL
    );
END;

IF OBJECT_ID('dbo.WorkflowLeadDedupeStates','U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkflowLeadDedupeStates](
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        [WorkflowId] UNIQUEIDENTIFIER NOT NULL,
        [ActionNodeId] NVARCHAR(120) NOT NULL,
        [DedupeKeyType] NVARCHAR(32) NOT NULL,
        [DedupeKeyValueHash] NVARCHAR(200) NOT NULL,
        [LastLeadId] NVARCHAR(128) NULL,
        [LastEventId] NVARCHAR(128) NULL,
        [LastProcessedAtUtc] DATETIME2 NOT NULL
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkflowLeadDedupeStates_Composite' AND object_id = OBJECT_ID('dbo.WorkflowLeadDedupeStates'))
    CREATE UNIQUE INDEX [IX_WorkflowLeadDedupeStates_Composite] ON [dbo].[WorkflowLeadDedupeStates]([WorkflowId],[ActionNodeId],[DedupeKeyType],[DedupeKeyValueHash]);

IF OBJECT_ID('dbo.WorkflowTriggerEventLogs','U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkflowTriggerEventLogs](
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        [WorkflowId] UNIQUEIDENTIFIER NOT NULL,
        [ActionNodeId] NVARCHAR(120) NOT NULL,
        [Provider] NVARCHAR(32) NOT NULL,
        [ExternalEventId] NVARCHAR(128) NULL,
        [LeadId] NVARCHAR(128) NULL,
        [Mode] NVARCHAR(24) NOT NULL,
        [Outcome] NVARCHAR(40) NOT NULL,
        [Reason] NVARCHAR(320) NULL,
        [PayloadJson] NVARCHAR(MAX) NULL,
        [ProcessedAtUtc] DATETIME2 NOT NULL
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkflowTriggerEventLogs_WorkflowId_ActionNodeId_ProcessedAtUtc' AND object_id = OBJECT_ID('dbo.WorkflowTriggerEventLogs'))
    CREATE INDEX [IX_WorkflowTriggerEventLogs_WorkflowId_ActionNodeId_ProcessedAtUtc] ON [dbo].[WorkflowTriggerEventLogs]([WorkflowId],[ActionNodeId],[ProcessedAtUtc]);
""");
    }
}
