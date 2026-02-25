using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class EmailOtpTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(12)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string Purpose { get; set; } = "signup";

    public int AttemptCount { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddMinutes(15);

    public DateTime? ConsumedAtUtc { get; set; }
}
