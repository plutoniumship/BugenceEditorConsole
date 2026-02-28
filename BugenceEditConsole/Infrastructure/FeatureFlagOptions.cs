namespace BugenceEditConsole.Infrastructure;

public sealed class FeatureFlagOptions
{
    public bool DynamicVeV1 { get; set; }
    public bool DynamicVeConfidenceV1 { get; set; }
    public bool DynamicVeInspectorProV1 { get; set; }
    public bool DynamicVeBindingDebugV1 { get; set; }
    public bool DynamicVeWorkflowV2 { get; set; } = true;
    public bool FacebookLeadTriggerV2 { get; set; } = true;
    public bool MetaWebhookLive { get; set; }
    public bool SocialMarketingV1 { get; set; } = true;
    public bool FacebookMarketingV1 { get; set; } = true;
}
