using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class TeamInvite
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string OwnerUserId { get; set; } = string.Empty;

    [Required, MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(180)]
    public string? DisplayNameHint { get; set; }

    [Required, MaxLength(32)]
    public string Role { get; set; } = "Editor";

    [Required, MaxLength(120)]
    public string Token { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddDays(7);

    public DateTime? ConsumedAtUtc { get; set; }
}
