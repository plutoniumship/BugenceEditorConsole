namespace BugenceEditConsole.Models;

public class WorkflowTriggerRequest
{
    public Guid WorkflowId { get; set; }

    public string? WorkflowDguid { get; set; }

    public string? Email { get; set; }

    public Dictionary<string, string?> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? SourceUrl { get; set; }

    public string? ElementTag { get; set; }

    public string? ElementId { get; set; }
}
