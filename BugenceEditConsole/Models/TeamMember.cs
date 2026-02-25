using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class TeamMember
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string OwnerUserId { get; set; } = string.Empty;

    public string? UserId { get; set; }

    [Required, MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(180)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? Title { get; set; }

    [MaxLength(32)]
    public string? Phone { get; set; }

    [Required, MaxLength(32)]
    public string Role { get; set; } = "Editor";

    [Required, MaxLength(32)]
    public string Status { get; set; } = "Active";

    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastActiveAtUtc { get; set; }
}
