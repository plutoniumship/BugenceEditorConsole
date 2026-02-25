using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class DynamicVePageRevision
{
    public long Id { get; set; }

    public int UploadedProjectId { get; set; }

    [Required, MaxLength(512)]
    public string PagePath { get; set; } = "index.html";

    [Required, MaxLength(32)]
    public string Environment { get; set; } = "draft";

    [Required, MaxLength(32)]
    public string Status { get; set; } = "draft";

    public long? BaseSnapshotId { get; set; }

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public UploadedProject? Project { get; set; }
}

