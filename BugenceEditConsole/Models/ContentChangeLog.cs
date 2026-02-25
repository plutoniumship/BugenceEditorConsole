using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class ContentChangeLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid SitePageId { get; set; }

    public Guid? PageSectionId { get; set; }

    [MaxLength(256)]
    public string FieldKey { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string? PreviousValue { get; set; }

    [MaxLength(1024)]
    public string? NewValue { get; set; }

    public string? ChangeSummary { get; set; }

    [Required]
    public string PerformedByUserId { get; set; } = string.Empty;

    [MaxLength(180)]
    public string? PerformedByDisplayName { get; set; }

    public DateTime PerformedAtUtc { get; set; } = DateTime.UtcNow;
}
