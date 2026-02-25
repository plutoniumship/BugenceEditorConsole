using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class DynamicVeAuditLog
{
    public long Id { get; set; }

    public int ProjectId { get; set; }

    public long? RevisionId { get; set; }

    [MaxLength(450)]
    public string? ActorUserId { get; set; }

    [Required, MaxLength(64)]
    public string Action { get; set; } = string.Empty;

    [Required]
    public string PayloadJson { get; set; } = "{}";

    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}

