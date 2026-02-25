using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class ProjectPreflightRun
{
    public long Id { get; set; }

    public int UploadedProjectId { get; set; }

    [Required, MaxLength(512)]
    public string FilePath { get; set; } = "index.html";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int Score { get; set; } = 100;

    public bool Safe { get; set; } = true;

    [Required]
    public string BlockersJson { get; set; } = "[]";

    [Required]
    public string WarningsJson { get; set; } = "[]";

    [Required]
    public string DiffSummaryJson { get; set; } = "{}";

    public UploadedProject? Project { get; set; }
}

