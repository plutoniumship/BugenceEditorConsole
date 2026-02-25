using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class ProjectEnvironmentPointer
{
    public long Id { get; set; }

    public int UploadedProjectId { get; set; }

    public long? DraftSnapshotId { get; set; }
    public long? StagingSnapshotId { get; set; }
    public long? LiveSnapshotId { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public UploadedProject? Project { get; set; }
    public ProjectDeploySnapshot? DraftSnapshot { get; set; }
    public ProjectDeploySnapshot? StagingSnapshot { get; set; }
    public ProjectDeploySnapshot? LiveSnapshot { get; set; }
}

