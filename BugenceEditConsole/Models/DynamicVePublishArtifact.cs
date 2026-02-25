using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class DynamicVePublishArtifact
{
    public long Id { get; set; }

    public long RevisionId { get; set; }

    [Required, MaxLength(32)]
    public string ArtifactType { get; set; } = "overlay-package";

    [Required, MaxLength(1024)]
    public string ArtifactPath { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Checksum { get; set; } = string.Empty;

    public DateTime PublishedAtUtc { get; set; } = DateTime.UtcNow;

    public DynamicVePageRevision? Revision { get; set; }
}

