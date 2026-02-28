using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class IntegrationConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? WorkspaceId { get; set; }
    [MaxLength(64)] public string Provider { get; set; } = "facebook";
    [MaxLength(180)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(180)] public string ExternalAccountId { get; set; } = string.Empty;
    [MaxLength(32)] public string Status { get; set; } = "connected";
    public string? ScopesJson { get; set; }
    public string? AccessTokenEncrypted { get; set; }
    public string? RefreshTokenEncrypted { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string? MetadataJson { get; set; }
    [Required, MaxLength(450)] public string OwnerUserId { get; set; } = string.Empty;
    public Guid? CompanyId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class FacebookIntegrationAssetCache
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConnectionId { get; set; }
    [MaxLength(32)] public string AssetType { get; set; } = string.Empty;
    [MaxLength(180)] public string ExternalId { get; set; } = string.Empty;
    [MaxLength(280)] public string Name { get; set; } = string.Empty;
    [MaxLength(180)] public string? ParentExternalId { get; set; }
    public string? RawJson { get; set; }
    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;
}

public class WorkflowTriggerConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    [MaxLength(120)] public string ActionNodeId { get; set; } = string.Empty;
    [MaxLength(64)] public string TriggerType { get; set; } = "facebook_lead";
    [MaxLength(24)] public string Mode { get; set; } = "test";
    public Guid? ConnectionId { get; set; }
    [MaxLength(64)] public string? AdAccountId { get; set; }
    [MaxLength(64)] public string? PageId { get; set; }
    [MaxLength(64)] public string? FormId { get; set; }
    [MaxLength(64)] public string TriggerEvent { get; set; } = "leadgen.form.submit";
    public bool RequireConsent { get; set; }
    public int ReplayWindowMinutes { get; set; } = 10;
    [MaxLength(64)] public string MappingMode { get; set; } = "Lead Fields";
    public string MappingJson { get; set; } = "[]";
    public string ValidationConfigJson { get; set; } = "{}";
    public string OutputRoutingConfigJson { get; set; } = "{}";
    [Required, MaxLength(450)] public string OwnerUserId { get; set; } = string.Empty;
    public Guid? CompanyId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class WorkflowFieldMappingPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? WorkspaceId { get; set; }
    [MaxLength(140)] public string Name { get; set; } = string.Empty;
    [MaxLength(64)] public string TriggerType { get; set; } = "facebook_lead";
    [MaxLength(32)] public string TargetEntity { get; set; } = "lead";
    public string MappingJson { get; set; } = "[]";
    public bool IsDefault { get; set; }
    [Required, MaxLength(450)] public string OwnerUserId { get; set; } = string.Empty;
    public Guid? CompanyId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class WorkflowLeadDedupeState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    [MaxLength(120)] public string ActionNodeId { get; set; } = string.Empty;
    [MaxLength(32)] public string DedupeKeyType { get; set; } = string.Empty;
    [MaxLength(200)] public string DedupeKeyValueHash { get; set; } = string.Empty;
    [MaxLength(128)] public string? LastLeadId { get; set; }
    [MaxLength(128)] public string? LastEventId { get; set; }
    public DateTime LastProcessedAtUtc { get; set; } = DateTime.UtcNow;
}

public class WorkflowTriggerEventLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    [MaxLength(120)] public string ActionNodeId { get; set; } = string.Empty;
    [MaxLength(32)] public string Provider { get; set; } = "facebook";
    [MaxLength(128)] public string? ExternalEventId { get; set; }
    [MaxLength(128)] public string? LeadId { get; set; }
    [MaxLength(24)] public string Mode { get; set; } = "test";
    [MaxLength(40)] public string Outcome { get; set; } = "received";
    [MaxLength(320)] public string? Reason { get; set; }
    public string? PayloadJson { get; set; }
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class FacebookIntegrationAccountDto
{
    public Guid ConnectionId { get; set; }
    public string Provider { get; set; } = "facebook";
    public string DisplayName { get; set; } = string.Empty;
    public string ExternalAccountId { get; set; } = string.Empty;
    public string Status { get; set; } = "disconnected";
    public string[] Scopes { get; set; } = [];
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? LastSyncedAtUtc { get; set; }
}

public sealed class FacebookAssetDto
{
    public string Type { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ParentId { get; set; }
}

public sealed class LeadMappingRuleDto
{
    public string SourceField { get; set; } = string.Empty;
    public string TargetEntity { get; set; } = "lead";
    public string TargetField { get; set; } = string.Empty;
    public string? Transform { get; set; }
}

public sealed class LeadMappingPresetDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TriggerType { get; set; } = "facebook_lead";
    public string TargetEntity { get; set; } = "lead";
    public bool IsDefault { get; set; }
    public IReadOnlyList<LeadMappingRuleDto> Rules { get; set; } = [];
}

public sealed class TriggerValidationConfigDto
{
    public bool RequireConsent { get; set; }
    public int ReplayWindowMinutes { get; set; } = 10;
    public bool RouteMissingContactToNeedsEnrichment { get; set; } = true;
}

public sealed class TriggerRoutingDecisionDto
{
    public string BranchKey { get; set; } = "primary";
    public string Reason { get; set; } = "ok";
    public bool IsDuplicate { get; set; }
}

public sealed class FacebookLeadPayloadDto
{
    public string? LeadId { get; set; }
    public DateTimeOffset? CreatedTime { get; set; }
    public string? FormId { get; set; }
    public string? PageId { get; set; }
    public string? CampaignId { get; set; }
    public string? AdsetId { get; set; }
    public string? AdId { get; set; }
    public string Platform { get; set; } = "FB";
    public Dictionary<string, string?> FieldData { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string?> ConsentFlags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? CampaignName { get; set; }
    public string? AdsetName { get; set; }
    public string? AdName { get; set; }
    public string? Locale { get; set; }
    public string? Language { get; set; }
}

public sealed class FacebookLeadTriggerConfigDto
{
    public Guid WorkflowId { get; set; }
    public string ActionNodeId { get; set; } = string.Empty;
    public string TriggerType { get; set; } = "facebook_lead";
    public string Mode { get; set; } = "test";
    public Guid? ConnectionId { get; set; }
    public string? AdAccountId { get; set; }
    public string? PageId { get; set; }
    public string? FormId { get; set; }
    public string TriggerEvent { get; set; } = "leadgen.form.submit";
    public string MappingMode { get; set; } = "Lead Fields";
    public IReadOnlyList<LeadMappingRuleDto> MappingRules { get; set; } = [];
    public TriggerValidationConfigDto Validation { get; set; } = new();
    public bool SplitFullName { get; set; } = true;
    public bool NormalizePhone { get; set; } = true;
    public bool ValidateEmail { get; set; } = true;
    public bool DetectCountryFromPhone { get; set; } = true;
}

public sealed class FacebookLeadTriggerReplayRequest
{
    public bool EmitIntoWorkflow { get; set; }
    public Dictionary<string, string?>? MockFields { get; set; }
}
