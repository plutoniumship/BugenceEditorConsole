using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class UserProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(160)]
    public string? BusinessName { get; set; }

    [MaxLength(320)]
    public string? BusinessEmail { get; set; }

    [MaxLength(260)]
    public string? BusinessPhone { get; set; }

    [MaxLength(260)]
    public string? BusinessAddress { get; set; }

    [MaxLength(160)]
    public string? ClientName { get; set; }

    [MaxLength(320)]
    public string? ClientEmail { get; set; }

    [MaxLength(260)]
    public string? ClientPhone { get; set; }

    public string? ProfileImagePath { get; set; }

    public bool IsProfileLocked { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LockedAtUtc { get; set; }
}
