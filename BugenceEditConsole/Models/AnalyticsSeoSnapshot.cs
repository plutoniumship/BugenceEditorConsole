using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class AnalyticsSeoSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ProjectId { get; set; }
    [Required, MaxLength(450)] public string OwnerUserId { get; set; } = string.Empty;
    public Guid? CompanyId { get; set; }
    [MaxLength(512)] public string PropertyUri { get; set; } = string.Empty;
    [MaxLength(32)] public string SnapshotType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
}

