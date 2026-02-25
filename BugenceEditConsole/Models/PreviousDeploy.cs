using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class PreviousDeploy
{
    public int Id { get; set; }

    public int UploadedProjectId { get; set; }

    /// <summary>Serialized snapshot of file entries (path/size/isFolder) at time of deploy removal.</summary>
    [Required]
    public string PayloadJson { get; set; } = string.Empty;

    public DateTime StoredAtUtc { get; set; } = DateTime.UtcNow;
}
