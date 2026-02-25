using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class DynamicVeProjectConfig
{
    public long Id { get; set; }

    public int UploadedProjectId { get; set; }

    [MaxLength(32)]
    public string Mode { get; set; } = "overlay";

    [MaxLength(64)]
    public string RuntimePolicy { get; set; } = "proxy";

    public bool FeatureEnabled { get; set; } = true;

    public long? DraftRevisionId { get; set; }
    public long? StagingRevisionId { get; set; }
    public long? LiveRevisionId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public UploadedProject? Project { get; set; }
}

