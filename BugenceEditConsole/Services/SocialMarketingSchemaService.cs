using System.Data.Common;
using BugenceEditConsole.Data;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public static class SocialMarketingSchemaService
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
            await EnsureSqliteAsync(connection);
            return;
        }

        await EnsureSqlServerAsync(db);
    }

    private static async Task EnsureSqliteAsync(DbConnection connection)
    {
        var statements = new[]
        {
            """CREATE TABLE IF NOT EXISTS "MktMetaPages"("Id" TEXT NOT NULL PRIMARY KEY,"TenantId" TEXT NOT NULL,"WorkspaceId" TEXT NOT NULL,"IntegrationConnectionId" TEXT NOT NULL,"PageId" TEXT NOT NULL,"Name" TEXT NOT NULL,"RawJson" TEXT NULL,"SyncedAtUtc" TEXT NOT NULL);""",
            """CREATE TABLE IF NOT EXISTS "MktMetaForms"("Id" TEXT NOT NULL PRIMARY KEY,"TenantId" TEXT NOT NULL,"WorkspaceId" TEXT NOT NULL,"IntegrationConnectionId" TEXT NOT NULL,"PageId" TEXT NOT NULL,"FormId" TEXT NOT NULL,"Name" TEXT NOT NULL,"QuestionsJson" TEXT NULL,"SyncedAtUtc" TEXT NOT NULL);""",
            """CREATE TABLE IF NOT EXISTS "MktMetaCampaigns"("Id" TEXT NOT NULL PRIMARY KEY,"TenantId" TEXT NOT NULL,"WorkspaceId" TEXT NOT NULL,"IntegrationConnectionId" TEXT NOT NULL,"CampaignId" TEXT NOT NULL,"Name" TEXT NOT NULL,"Status" TEXT NOT NULL,"SyncedAtUtc" TEXT NOT NULL);""",
            """CREATE TABLE IF NOT EXISTS "MktMetaAdsets"("Id" TEXT NOT NULL PRIMARY KEY,"TenantId" TEXT NOT NULL,"WorkspaceId" TEXT NOT NULL,"IntegrationConnectionId" TEXT NOT NULL,"AdsetId" TEXT NOT NULL,"CampaignId" TEXT NOT NULL,"Name" TEXT NOT NULL,"Status" TEXT NOT NULL,"SyncedAtUtc" TEXT NOT NULL);""",
            """CREATE TABLE IF NOT EXISTS "MktMetaAds"("Id" TEXT NOT NULL PRIMARY KEY,"TenantId" TEXT NOT NULL,"WorkspaceId" TEXT NOT NULL,"IntegrationConnectionId" TEXT NOT NULL,"AdId" TEXT NOT NULL,"AdsetId" TEXT NOT NULL,"CampaignId" TEXT NOT NULL,"Name" TEXT NOT NULL,"Status" TEXT NOT NULL,"Platform" TEXT NOT NULL,"PageId" TEXT NULL,"FormId" TEXT NULL,"LastLeadAtUtc" TEXT NULL,"SyncedAtUtc" TEXT NOT NULL);""",
            """CREATE TABLE IF NOT EXISTS "MktMetaLeads"("Id" TEXT NOT NULL PRIMARY KEY,"TenantId" TEXT NOT NULL,"WorkspaceId" TEXT NOT NULL,"IntegrationConnectionId" TEXT NOT NULL,"LeadId" TEXT NOT NULL,"PageId" TEXT NULL,"FormId" TEXT NULL,"CampaignId" TEXT NULL,"AdsetId" TEXT NULL,"AdId" TEXT NULL,"Platform" TEXT NOT NULL,"CreatedTime" TEXT NOT NULL,"FieldDataJson" TEXT NOT NULL,"ConsentJson" TEXT NOT NULL,"NormalizedName" TEXT NULL,"NormalizedPhone" TEXT NULL,"NormalizedEmail" TEXT NULL,"Country" TEXT NULL,"City" TEXT NULL,"DedupeKeyEmail" TEXT NULL,"DedupeKeyPhone" TEXT NULL,"DedupeKeyLead" TEXT NOT NULL,"Status" TEXT NOT NULL,"AssignedUserId" TEXT NULL,"Score" INTEGER NULL,"LastActionAtUtc" TEXT NULL,"DuplicateOfLeadId" TEXT NULL,"RawJson" TEXT NULL,"CreatedAtUtc" TEXT NOT NULL,"UpdatedAtUtc" TEXT NOT NULL);""",
            """CREATE TABLE IF NOT EXISTS "MktLeadActivities"("Id" TEXT NOT NULL PRIMARY KEY,"TenantId" TEXT NOT NULL,"WorkspaceId" TEXT NOT NULL,"LeadPkId" TEXT NOT NULL,"Type" TEXT NOT NULL,"PayloadJson" TEXT NOT NULL,"CreatedBy" TEXT NOT NULL,"CreatedAtUtc" TEXT NOT NULL);""",
            """CREATE TABLE IF NOT EXISTS "MktAutoRules"("Id" TEXT NOT NULL PRIMARY KEY,"TenantId" TEXT NOT NULL,"WorkspaceId" TEXT NOT NULL,"IntegrationConnectionId" TEXT NOT NULL,"Channel" TEXT NOT NULL,"EventType" TEXT NOT NULL,"WorkflowId" TEXT NOT NULL,"IsEnabled" INTEGER NOT NULL DEFAULT 1,"ConditionsJson" TEXT NOT NULL,"CreatedAtUtc" TEXT NOT NULL,"UpdatedAtUtc" TEXT NOT NULL);""",
            """CREATE TABLE IF NOT EXISTS "MktWorkflowRuns"("Id" TEXT NOT NULL PRIMARY KEY,"TenantId" TEXT NOT NULL,"WorkspaceId" TEXT NOT NULL,"WorkflowId" TEXT NOT NULL,"TriggerType" TEXT NOT NULL,"SourceEntity" TEXT NOT NULL,"SourceId" TEXT NOT NULL,"Status" TEXT NOT NULL,"LogsJson" TEXT NOT NULL,"CreatedAtUtc" TEXT NOT NULL,"UpdatedAtUtc" TEXT NOT NULL);""",
            """CREATE TABLE IF NOT EXISTS "MktSyncStates"("Id" TEXT NOT NULL PRIMARY KEY,"TenantId" TEXT NOT NULL,"WorkspaceId" TEXT NOT NULL,"IntegrationConnectionId" TEXT NOT NULL,"Scope" TEXT NOT NULL,"LastSuccessAtUtc" TEXT NULL,"LastCursor" TEXT NULL,"LastDeepSyncAtUtc" TEXT NULL,"UpdatedAtUtc" TEXT NOT NULL);""",
            """CREATE TABLE IF NOT EXISTS "MktSyncLogs"("Id" TEXT NOT NULL PRIMARY KEY,"TenantId" TEXT NOT NULL,"WorkspaceId" TEXT NOT NULL,"IntegrationConnectionId" TEXT NOT NULL,"Scope" TEXT NOT NULL,"Status" TEXT NOT NULL,"StartedAtUtc" TEXT NOT NULL,"EndedAtUtc" TEXT NOT NULL,"CountsJson" TEXT NOT NULL,"ErrorJson" TEXT NULL);""",
            """CREATE TABLE IF NOT EXISTS "MktDeadLetterEvents"("Id" TEXT NOT NULL PRIMARY KEY,"TenantId" TEXT NOT NULL,"WorkspaceId" TEXT NOT NULL,"IntegrationConnectionId" TEXT NOT NULL,"EventType" TEXT NOT NULL,"PayloadJson" TEXT NOT NULL,"ErrorJson" TEXT NOT NULL,"Attempts" INTEGER NOT NULL DEFAULT 0,"CreatedAtUtc" TEXT NOT NULL,"ResolvedAtUtc" TEXT NULL);""",

            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_MktMetaPages_Key" ON "MktMetaPages"("TenantId","WorkspaceId","IntegrationConnectionId","PageId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_MktMetaForms_Key" ON "MktMetaForms"("TenantId","WorkspaceId","IntegrationConnectionId","FormId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_MktMetaCampaigns_Key" ON "MktMetaCampaigns"("TenantId","WorkspaceId","IntegrationConnectionId","CampaignId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_MktMetaAdsets_Key" ON "MktMetaAdsets"("TenantId","WorkspaceId","IntegrationConnectionId","AdsetId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_MktMetaAds_Key" ON "MktMetaAds"("TenantId","WorkspaceId","IntegrationConnectionId","AdId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_MktMetaLeads_Idempotency" ON "MktMetaLeads"("TenantId","WorkspaceId","IntegrationConnectionId","LeadId");""",
            """CREATE INDEX IF NOT EXISTS "IX_MktMetaLeads_AdTime" ON "MktMetaLeads"("TenantId","WorkspaceId","AdId","CreatedTime");""",
            """CREATE INDEX IF NOT EXISTS "IX_MktMetaLeads_Email" ON "MktMetaLeads"("TenantId","WorkspaceId","NormalizedEmail");""",
            """CREATE INDEX IF NOT EXISTS "IX_MktMetaLeads_Phone" ON "MktMetaLeads"("TenantId","WorkspaceId","NormalizedPhone");""",
            """CREATE INDEX IF NOT EXISTS "IX_MktMetaLeads_Status" ON "MktMetaLeads"("TenantId","WorkspaceId","Status");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_MktSyncStates_Key" ON "MktSyncStates"("TenantId","WorkspaceId","IntegrationConnectionId","Scope");"""
        };

        foreach (var statement in statements)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = statement;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task EnsureSqlServerAsync(ApplicationDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
IF OBJECT_ID('dbo.MktMetaPages','U') IS NULL CREATE TABLE [dbo].[MktMetaPages]([Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,[TenantId] UNIQUEIDENTIFIER NOT NULL,[WorkspaceId] UNIQUEIDENTIFIER NOT NULL,[IntegrationConnectionId] UNIQUEIDENTIFIER NOT NULL,[PageId] NVARCHAR(128) NOT NULL,[Name] NVARCHAR(280) NOT NULL,[RawJson] NVARCHAR(MAX) NULL,[SyncedAtUtc] DATETIME2 NOT NULL);
IF OBJECT_ID('dbo.MktMetaForms','U') IS NULL CREATE TABLE [dbo].[MktMetaForms]([Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,[TenantId] UNIQUEIDENTIFIER NOT NULL,[WorkspaceId] UNIQUEIDENTIFIER NOT NULL,[IntegrationConnectionId] UNIQUEIDENTIFIER NOT NULL,[PageId] NVARCHAR(128) NOT NULL,[FormId] NVARCHAR(128) NOT NULL,[Name] NVARCHAR(280) NOT NULL,[QuestionsJson] NVARCHAR(MAX) NULL,[SyncedAtUtc] DATETIME2 NOT NULL);
IF OBJECT_ID('dbo.MktMetaCampaigns','U') IS NULL CREATE TABLE [dbo].[MktMetaCampaigns]([Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,[TenantId] UNIQUEIDENTIFIER NOT NULL,[WorkspaceId] UNIQUEIDENTIFIER NOT NULL,[IntegrationConnectionId] UNIQUEIDENTIFIER NOT NULL,[CampaignId] NVARCHAR(128) NOT NULL,[Name] NVARCHAR(280) NOT NULL,[Status] NVARCHAR(32) NOT NULL,[SyncedAtUtc] DATETIME2 NOT NULL);
IF OBJECT_ID('dbo.MktMetaAdsets','U') IS NULL CREATE TABLE [dbo].[MktMetaAdsets]([Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,[TenantId] UNIQUEIDENTIFIER NOT NULL,[WorkspaceId] UNIQUEIDENTIFIER NOT NULL,[IntegrationConnectionId] UNIQUEIDENTIFIER NOT NULL,[AdsetId] NVARCHAR(128) NOT NULL,[CampaignId] NVARCHAR(128) NOT NULL,[Name] NVARCHAR(280) NOT NULL,[Status] NVARCHAR(32) NOT NULL,[SyncedAtUtc] DATETIME2 NOT NULL);
IF OBJECT_ID('dbo.MktMetaAds','U') IS NULL CREATE TABLE [dbo].[MktMetaAds]([Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,[TenantId] UNIQUEIDENTIFIER NOT NULL,[WorkspaceId] UNIQUEIDENTIFIER NOT NULL,[IntegrationConnectionId] UNIQUEIDENTIFIER NOT NULL,[AdId] NVARCHAR(128) NOT NULL,[AdsetId] NVARCHAR(128) NOT NULL,[CampaignId] NVARCHAR(128) NOT NULL,[Name] NVARCHAR(280) NOT NULL,[Status] NVARCHAR(32) NOT NULL,[Platform] NVARCHAR(16) NOT NULL,[PageId] NVARCHAR(128) NULL,[FormId] NVARCHAR(128) NULL,[LastLeadAtUtc] DATETIME2 NULL,[SyncedAtUtc] DATETIME2 NOT NULL);
IF OBJECT_ID('dbo.MktMetaLeads','U') IS NULL CREATE TABLE [dbo].[MktMetaLeads]([Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,[TenantId] UNIQUEIDENTIFIER NOT NULL,[WorkspaceId] UNIQUEIDENTIFIER NOT NULL,[IntegrationConnectionId] UNIQUEIDENTIFIER NOT NULL,[LeadId] NVARCHAR(128) NOT NULL,[PageId] NVARCHAR(128) NULL,[FormId] NVARCHAR(128) NULL,[CampaignId] NVARCHAR(128) NULL,[AdsetId] NVARCHAR(128) NULL,[AdId] NVARCHAR(128) NULL,[Platform] NVARCHAR(16) NOT NULL,[CreatedTime] DATETIMEOFFSET NOT NULL,[FieldDataJson] NVARCHAR(MAX) NOT NULL,[ConsentJson] NVARCHAR(MAX) NOT NULL,[NormalizedName] NVARCHAR(280) NULL,[NormalizedPhone] NVARCHAR(128) NULL,[NormalizedEmail] NVARCHAR(280) NULL,[Country] NVARCHAR(16) NULL,[City] NVARCHAR(120) NULL,[DedupeKeyEmail] NVARCHAR(280) NULL,[DedupeKeyPhone] NVARCHAR(128) NULL,[DedupeKeyLead] NVARCHAR(128) NOT NULL,[Status] NVARCHAR(40) NOT NULL,[AssignedUserId] NVARCHAR(450) NULL,[Score] INT NULL,[LastActionAtUtc] DATETIME2 NULL,[DuplicateOfLeadId] UNIQUEIDENTIFIER NULL,[RawJson] NVARCHAR(MAX) NULL,[CreatedAtUtc] DATETIME2 NOT NULL,[UpdatedAtUtc] DATETIME2 NOT NULL);
IF OBJECT_ID('dbo.MktLeadActivities','U') IS NULL CREATE TABLE [dbo].[MktLeadActivities]([Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,[TenantId] UNIQUEIDENTIFIER NOT NULL,[WorkspaceId] UNIQUEIDENTIFIER NOT NULL,[LeadPkId] UNIQUEIDENTIFIER NOT NULL,[Type] NVARCHAR(48) NOT NULL,[PayloadJson] NVARCHAR(MAX) NOT NULL,[CreatedBy] NVARCHAR(450) NOT NULL,[CreatedAtUtc] DATETIME2 NOT NULL);
IF OBJECT_ID('dbo.MktAutoRules','U') IS NULL CREATE TABLE [dbo].[MktAutoRules]([Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,[TenantId] UNIQUEIDENTIFIER NOT NULL,[WorkspaceId] UNIQUEIDENTIFIER NOT NULL,[IntegrationConnectionId] UNIQUEIDENTIFIER NOT NULL,[Channel] NVARCHAR(32) NOT NULL,[EventType] NVARCHAR(64) NOT NULL,[WorkflowId] UNIQUEIDENTIFIER NOT NULL,[IsEnabled] BIT NOT NULL DEFAULT 1,[ConditionsJson] NVARCHAR(MAX) NOT NULL,[CreatedAtUtc] DATETIME2 NOT NULL,[UpdatedAtUtc] DATETIME2 NOT NULL);
IF OBJECT_ID('dbo.MktWorkflowRuns','U') IS NULL CREATE TABLE [dbo].[MktWorkflowRuns]([Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,[TenantId] UNIQUEIDENTIFIER NOT NULL,[WorkspaceId] UNIQUEIDENTIFIER NOT NULL,[WorkflowId] UNIQUEIDENTIFIER NOT NULL,[TriggerType] NVARCHAR(64) NOT NULL,[SourceEntity] NVARCHAR(32) NOT NULL,[SourceId] UNIQUEIDENTIFIER NOT NULL,[Status] NVARCHAR(24) NOT NULL,[LogsJson] NVARCHAR(MAX) NOT NULL,[CreatedAtUtc] DATETIME2 NOT NULL,[UpdatedAtUtc] DATETIME2 NOT NULL);
IF OBJECT_ID('dbo.MktSyncStates','U') IS NULL CREATE TABLE [dbo].[MktSyncStates]([Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,[TenantId] UNIQUEIDENTIFIER NOT NULL,[WorkspaceId] UNIQUEIDENTIFIER NOT NULL,[IntegrationConnectionId] UNIQUEIDENTIFIER NOT NULL,[Scope] NVARCHAR(40) NOT NULL,[LastSuccessAtUtc] DATETIME2 NULL,[LastCursor] NVARCHAR(MAX) NULL,[LastDeepSyncAtUtc] DATETIME2 NULL,[UpdatedAtUtc] DATETIME2 NOT NULL);
IF OBJECT_ID('dbo.MktSyncLogs','U') IS NULL CREATE TABLE [dbo].[MktSyncLogs]([Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,[TenantId] UNIQUEIDENTIFIER NOT NULL,[WorkspaceId] UNIQUEIDENTIFIER NOT NULL,[IntegrationConnectionId] UNIQUEIDENTIFIER NOT NULL,[Scope] NVARCHAR(40) NOT NULL,[Status] NVARCHAR(24) NOT NULL,[StartedAtUtc] DATETIME2 NOT NULL,[EndedAtUtc] DATETIME2 NOT NULL,[CountsJson] NVARCHAR(MAX) NOT NULL,[ErrorJson] NVARCHAR(MAX) NULL);
IF OBJECT_ID('dbo.MktDeadLetterEvents','U') IS NULL CREATE TABLE [dbo].[MktDeadLetterEvents]([Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,[TenantId] UNIQUEIDENTIFIER NOT NULL,[WorkspaceId] UNIQUEIDENTIFIER NOT NULL,[IntegrationConnectionId] UNIQUEIDENTIFIER NOT NULL,[EventType] NVARCHAR(48) NOT NULL,[PayloadJson] NVARCHAR(MAX) NOT NULL,[ErrorJson] NVARCHAR(MAX) NOT NULL,[Attempts] INT NOT NULL DEFAULT 0,[CreatedAtUtc] DATETIME2 NOT NULL,[ResolvedAtUtc] DATETIME2 NULL);
""");
    }
}
