using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace BugenceEditConsole.Models;

public class MktMetaPage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    [MaxLength(128)] public string PageId { get; set; } = string.Empty;
    [MaxLength(280)] public string Name { get; set; } = string.Empty;
    public string? RawJson { get; set; }
    public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
}

public class MktMetaForm
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    [MaxLength(128)] public string PageId { get; set; } = string.Empty;
    [MaxLength(128)] public string FormId { get; set; } = string.Empty;
    [MaxLength(280)] public string Name { get; set; } = string.Empty;
    public string? QuestionsJson { get; set; }
    public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
}

public class MktMetaCampaign
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    [MaxLength(128)] public string CampaignId { get; set; } = string.Empty;
    [MaxLength(280)] public string Name { get; set; } = string.Empty;
    [MaxLength(32)] public string Status { get; set; } = "active";
    public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
}

public class MktMetaAdset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    [MaxLength(128)] public string AdsetId { get; set; } = string.Empty;
    [MaxLength(128)] public string CampaignId { get; set; } = string.Empty;
    [MaxLength(280)] public string Name { get; set; } = string.Empty;
    [MaxLength(32)] public string Status { get; set; } = "active";
    public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
}

public class MktMetaAd
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    [MaxLength(128)] public string AdId { get; set; } = string.Empty;
    [MaxLength(128)] public string AdsetId { get; set; } = string.Empty;
    [MaxLength(128)] public string CampaignId { get; set; } = string.Empty;
    [MaxLength(280)] public string Name { get; set; } = string.Empty;
    [MaxLength(32)] public string Status { get; set; } = "active";
    [MaxLength(16)] public string Platform { get; set; } = "fb";
    [MaxLength(128)] public string? PageId { get; set; }
    [MaxLength(128)] public string? FormId { get; set; }
    public DateTime? LastLeadAtUtc { get; set; }
    public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
}

public class MktMetaLead
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    [MaxLength(128)] public string LeadId { get; set; } = string.Empty;
    [MaxLength(128)] public string? PageId { get; set; }
    [MaxLength(128)] public string? FormId { get; set; }
    [MaxLength(128)] public string? CampaignId { get; set; }
    [MaxLength(128)] public string? AdsetId { get; set; }
    [MaxLength(128)] public string? AdId { get; set; }
    [MaxLength(16)] public string Platform { get; set; } = "fb";
    public DateTimeOffset CreatedTime { get; set; } = DateTimeOffset.UtcNow;
    public string FieldDataJson { get; set; } = "{}";
    public string ConsentJson { get; set; } = "{}";
    [MaxLength(280)] public string? NormalizedName { get; set; }
    [MaxLength(128)] public string? NormalizedPhone { get; set; }
    [MaxLength(280)] public string? NormalizedEmail { get; set; }
    [MaxLength(16)] public string? Country { get; set; }
    [MaxLength(120)] public string? City { get; set; }
    [MaxLength(280)] public string? DedupeKeyEmail { get; set; }
    [MaxLength(128)] public string? DedupeKeyPhone { get; set; }
    [MaxLength(128)] public string DedupeKeyLead { get; set; } = string.Empty;
    [MaxLength(40)] public string Status { get; set; } = "new";
    [MaxLength(450)] public string? AssignedUserId { get; set; }
    public int? Score { get; set; }
    public DateTime? LastActionAtUtc { get; set; }
    public Guid? DuplicateOfLeadId { get; set; }
    public string? RawJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class MktLeadActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid LeadPkId { get; set; }
    [MaxLength(48)] public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    [MaxLength(450)] public string CreatedBy { get; set; } = "system";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class MktAutoRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    [MaxLength(32)] public string Channel { get; set; } = "facebook";
    [MaxLength(64)] public string EventType { get; set; } = "lead_received";
    public Guid WorkflowId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string ConditionsJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class MktWorkflowRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid WorkflowId { get; set; }
    [MaxLength(64)] public string TriggerType { get; set; } = string.Empty;
    [MaxLength(32)] public string SourceEntity { get; set; } = "mkt_lead";
    public Guid SourceId { get; set; }
    [MaxLength(24)] public string Status { get; set; } = "running";
    public string LogsJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class MktSyncState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    [MaxLength(40)] public string Scope { get; set; } = string.Empty;
    public DateTime? LastSuccessAtUtc { get; set; }
    public string? LastCursor { get; set; }
    public DateTime? LastDeepSyncAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class MktSyncLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    [MaxLength(40)] public string Scope { get; set; } = string.Empty;
    [MaxLength(24)] public string Status { get; set; } = "success";
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime EndedAtUtc { get; set; } = DateTime.UtcNow;
    public string CountsJson { get; set; } = "{}";
    public string? ErrorJson { get; set; }
}

public class MktDeadLetterEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    [MaxLength(48)] public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string ErrorJson { get; set; } = "{}";
    public int Attempts { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
}

public sealed class LeadContext
{
    public string Version { get; set; } = "1.0";
    public LeadContextSource Source { get; set; } = new();
    public LeadContextAttribution Attribution { get; set; } = new();
    public LeadContextLead Lead { get; set; } = new();
    public LeadContextBugence Bugence { get; set; } = new();
}

public sealed class LeadContextSource
{
    public string Channel { get; set; } = "facebook";
    public string Platform { get; set; } = "fb";
    public string Provider { get; set; } = "meta";
    public string IntegrationConnectionId { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public string EventType { get; set; } = "leadgen.form.submit";
    public string Mode { get; set; } = "webhook";
}

public sealed class LeadContextAttribution
{
    public LeadRef Page { get; set; } = new();
    public LeadRef Form { get; set; } = new();
    public LeadRef Campaign { get; set; } = new();
    public LeadRef Adset { get; set; } = new();
    public LeadRef Ad { get; set; } = new();
    public Dictionary<string, string?> Utm { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LeadContextLead
{
    public string ProviderLeadId { get; set; } = string.Empty;
    public DateTimeOffset CreatedTime { get; set; } = DateTimeOffset.UtcNow;
    public LeadConsent Consent { get; set; } = new();
    public LeadIdentity Identity { get; set; } = new();
    public List<LeadAnswer> Answers { get; set; } = [];
    public Dictionary<string, object?> Raw { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LeadContextBugence
{
    public string TenantId { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string MarketingLeadId { get; set; } = string.Empty;
    public Dictionary<string, string?> Crm { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> Owner { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Status { get; set; } = "new";
    public int? Score { get; set; }
}

public sealed class LeadRef
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class LeadConsent
{
    public bool HasConsent { get; set; }
    public Dictionary<string, object?> Raw { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LeadIdentity
{
    public string? FullName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? Language { get; set; }
}

public sealed class LeadAnswer
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = "text";
    public string? Value { get; set; }
}

public sealed class ActionResultContract
{
    public bool Ok { get; set; }
    public string Action { get; set; } = string.Empty;
    public string LeadId { get; set; } = string.Empty;
    public string? ProviderMessageId { get; set; }
    public string LoggedActivityId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WorkflowInvocationContract
{
    public Guid WorkflowId { get; set; }
    public LeadContextInput Input { get; set; } = new();
}

public sealed class LeadContextInput
{
    public LeadContext LeadContext { get; set; } = new();
}

public static class SocialMarketingJson
{
    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value);
}
