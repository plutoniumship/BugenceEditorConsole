using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class DynamicVeElementMap
{
    public long Id { get; set; }

    public long RevisionId { get; set; }

    [Required, MaxLength(160)]
    public string ElementKey { get; set; } = string.Empty;

    [Required, MaxLength(1024)]
    public string PrimarySelector { get; set; } = string.Empty;

    public string FallbackSelectorsJson { get; set; } = "[]";

    [MaxLength(128)]
    public string FingerprintHash { get; set; } = string.Empty;

    [MaxLength(128)]
    public string AnchorHash { get; set; } = string.Empty;

    public decimal Confidence { get; set; } = 1m;

    [MaxLength(1024)]
    public string? LastResolvedSelector { get; set; }

    public DateTime? LastResolvedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DynamicVePageRevision? Revision { get; set; }
}
