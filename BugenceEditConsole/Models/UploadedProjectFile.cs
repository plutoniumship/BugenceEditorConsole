using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BugenceEditConsole.Models;

public class UploadedProjectFile
{
    public int Id { get; set; }

    [Required]
    public int UploadedProjectId { get; set; }

    [ForeignKey(nameof(UploadedProjectId))]
    public UploadedProject Project { get; set; } = null!;

    /// <summary>
    /// Relative path using "/" separators (e.g., "folder/sub/file.html"). Directories end without trailing slash.
    /// </summary>
    [Required, MaxLength(1024)]
    public string RelativePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public bool IsFolder { get; set; }
}
