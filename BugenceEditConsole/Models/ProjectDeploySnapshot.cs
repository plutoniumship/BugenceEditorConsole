using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class ProjectDeploySnapshot
{
    public long Id { get; set; }

    public int UploadedProjectId { get; set; }

    [Required, MaxLength(32)]
    public string Environment { get; set; } = "draft";

    [MaxLength(128)]
    public string? VersionLabel { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    [Required]
    public string ManifestJson { get; set; } = "[]";

    [Required, MaxLength(1024)]
    public string RootPath { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string Source { get; set; } = "unknown";

    public bool IsSuccessful { get; set; } = true;

    public UploadedProject? Project { get; set; }
}

