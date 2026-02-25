using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BugenceEditConsole.Models;

public enum SectionContentType
{
    Text = 0,
    Html = 1,
    Image = 2,
    RichText = 3
}

public class PageSection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid SitePageId { get; set; }

    [ForeignKey(nameof(SitePageId))]
    public SitePage SitePage { get; set; } = default!;

    [Required, MaxLength(160)]
    public string SectionKey { get; set; } = string.Empty;

    [MaxLength(160)]
    public string? Title { get; set; }

    public SectionContentType ContentType { get; set; } = SectionContentType.Text;

    [DataType(DataType.MultilineText)]
    public string? ContentValue { get; set; }

    [MaxLength(512)]
    public string? CssSelector { get; set; }

    public string? MediaPath { get; set; }

    public string? MediaAltText { get; set; }

    [Range(0, 100)]
    public int DisplayOrder { get; set; }

    public bool IsLocked { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastPublishedAtUtc { get; set; }
}
