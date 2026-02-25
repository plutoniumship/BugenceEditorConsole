using System.Text.Json;

namespace BugenceEditConsole.Models;

public sealed class DynamicVeSessionStartRequest
{
    public int ProjectId { get; set; }
    public string? PagePath { get; set; }
}

public sealed class DynamicVeSaveDraftRequest
{
    public int ProjectId { get; set; }
    public string PagePath { get; set; } = "index.html";
    public JsonElement? Patch { get; set; }
}

public sealed class DynamicVePublishRequest
{
    public int ProjectId { get; set; }
    public string PagePath { get; set; } = "index.html";
    public bool OverrideRisk { get; set; }
}

public sealed class DynamicVeRollbackRequest
{
    public int ProjectId { get; set; }
    public string? PagePath { get; set; }
    public long? RevisionId { get; set; }
}

public sealed class DynamicVeBindActionRequest
{
    public int ProjectId { get; set; }
    public string PagePath { get; set; } = "index.html";
    public string ElementKey { get; set; } = string.Empty;
    public string ActionType { get; set; } = "navigate";
    public Guid? WorkflowId { get; set; }
    public string? NavigateUrl { get; set; }
    public JsonElement? Behavior { get; set; }
}

public sealed class DynamicVeResolveElementRequest
{
    public int ProjectId { get; set; }
    public string PagePath { get; set; } = "index.html";
    public string? ElementKey { get; set; }
    public string Selector { get; set; } = string.Empty;
    public List<string>? FallbackSelectors { get; set; }
    public string? FingerprintHash { get; set; }
    public string? AnchorHash { get; set; }
}

public sealed class DynamicVeEditTextRequest
{
    public int ProjectId { get; set; }
    public string PagePath { get; set; } = "index.html";
    public string ElementKey { get; set; } = string.Empty;
    public string Selector { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string TextMode { get; set; } = "plain";
}

public sealed class DynamicVeEditStyleRequest
{
    public int ProjectId { get; set; }
    public string PagePath { get; set; } = "index.html";
    public string ElementKey { get; set; } = string.Empty;
    public string Selector { get; set; } = string.Empty;
    public string Property { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Breakpoint { get; set; } = "desktop";
    public string State { get; set; } = "base";
}

public sealed class DynamicVeSectionInsertRequest
{
    public int ProjectId { get; set; }
    public string PagePath { get; set; } = "index.html";
    public string TemplateId { get; set; } = "custom";
    public string InsertMode { get; set; } = "after";
    public string? TargetElementKey { get; set; }
    public string Markup { get; set; } = string.Empty;
    public string? Css { get; set; }
    public string? Js { get; set; }
}

public sealed class DynamicVeBindTestRequest
{
    public int ProjectId { get; set; }
    public string PagePath { get; set; } = "index.html";
    public string ElementKey { get; set; } = string.Empty;
    public Dictionary<string, string?>? MockFields { get; set; }
    public string? MockEmail { get; set; }
}
