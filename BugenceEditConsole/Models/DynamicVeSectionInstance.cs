using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class DynamicVeSectionInstance
{
    public long Id { get; set; }

    public long RevisionId { get; set; }

    [Required, MaxLength(120)]
    public string TemplateId { get; set; } = "custom";

    [Required, MaxLength(24)]
    public string InsertMode { get; set; } = "after";

    [MaxLength(160)]
    public string TargetElementKey { get; set; } = string.Empty;

    [Required]
    public string MarkupJson { get; set; } = "{}";

    public string CssJson { get; set; } = "{}";
    public string JsJson { get; set; } = "{}";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DynamicVePageRevision? Revision { get; set; }
}

