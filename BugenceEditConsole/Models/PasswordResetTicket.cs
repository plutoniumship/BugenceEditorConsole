using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class PasswordResetTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, MaxLength(16)]
    public string VerificationCode { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddHours(1);

    public bool IsConsumed { get; set; }
}
