using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class UploadedProject
{
    public int Id { get; set; }

    [Required, MaxLength(256)]
    public string FolderName { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? DisplayName { get; set; }

    [MaxLength(1024)]
    public string? RepoUrl { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(1024)]
    public string? LocalPreviewPath { get; set; }

    public string? PageRouteOverridesJson { get; set; }

    public bool AutoDeployOnPush { get; set; } = true;

    public bool EnablePreviewDeploys { get; set; }

    public bool EnforceHttps { get; set; } = true;

    public bool EnableContentSecurityPolicy { get; set; }

    [MaxLength(450)]
    public string? UserId { get; set; }

    public Guid? CompanyId { get; set; }

    [Required, MaxLength(512)]
    public string OriginalFileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(64)]
    public string Status { get; set; } = "Uploaded";

    [MaxLength(512)]
    public string? PublishStoragePath { get; set; }

    public DateTime? LastPublishedAtUtc { get; set; }

    // Raw file payload (e.g., zip) stored for retrieval/reference.
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public ICollection<UploadedProjectFile> Files { get; set; } = new List<UploadedProjectFile>();

    public ICollection<ProjectDomain> Domains { get; set; } = new List<ProjectDomain>();

    public ICollection<ProjectDeploySnapshot> DeploySnapshots { get; set; } = new List<ProjectDeploySnapshot>();

    public ICollection<ProjectPreflightRun> PreflightRuns { get; set; } = new List<ProjectPreflightRun>();

    public ProjectEnvironmentPointer? EnvironmentPointer { get; set; }
    public DynamicVeProjectConfig? DynamicVeConfig { get; set; }
    public ICollection<DynamicVePageRevision> DynamicVeRevisions { get; set; } = new List<DynamicVePageRevision>();

    public CompanyProfile? Company { get; set; }
}
