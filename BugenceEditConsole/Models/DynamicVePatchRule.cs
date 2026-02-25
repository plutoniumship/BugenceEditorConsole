using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class DynamicVePatchRule
{
    public long Id { get; set; }

    public long RevisionId { get; set; }

    [Required, MaxLength(160)]
    public string ElementKey { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string RuleType { get; set; } = "style";

    [Required, MaxLength(24)]
    public string Breakpoint { get; set; } = "desktop";

    [Required, MaxLength(24)]
    public string State { get; set; } = "base";

    [Required, MaxLength(128)]
    public string Property { get; set; } = string.Empty;

    [Required]
    public string Value { get; set; } = string.Empty;

    public int Priority { get; set; } = 0;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DynamicVePageRevision? Revision { get; set; }
}

