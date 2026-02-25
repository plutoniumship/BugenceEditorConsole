using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class DynamicVeTextPatch
{
    public long Id { get; set; }

    public long RevisionId { get; set; }

    [Required, MaxLength(160)]
    public string ElementKey { get; set; } = string.Empty;

    [Required, MaxLength(24)]
    public string TextMode { get; set; } = "plain";

    [Required]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DynamicVePageRevision? Revision { get; set; }
}

