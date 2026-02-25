using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class DynamicVeActionBinding
{
    public long Id { get; set; }

    public long RevisionId { get; set; }

    [Required, MaxLength(160)]
    public string ElementKey { get; set; } = string.Empty;

    [Required, MaxLength(24)]
    public string ActionType { get; set; } = "navigate";

    public Guid? WorkflowId { get; set; }

    [MaxLength(2048)]
    public string? NavigateUrl { get; set; }

    public string BehaviorJson { get; set; } = "{}";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DynamicVePageRevision? Revision { get; set; }
}

