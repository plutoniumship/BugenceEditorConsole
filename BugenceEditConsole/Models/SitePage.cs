using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BugenceEditConsole.Models;

public class SitePage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(320)]
    public string? Description { get; set; }

    public string? HeroImagePath { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [InverseProperty(nameof(PageSection.SitePage))]
    public ICollection<PageSection> Sections { get; set; } = new List<PageSection>();

    public void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
