using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class UserPaymentMethod
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Provider { get; set; } = "None";

    [MaxLength(20)]
    public string? Brand { get; set; }

    [MaxLength(4)]
    public string? Last4 { get; set; }

    [MaxLength(2)]
    public string? ExpMonth { get; set; }

    [MaxLength(4)]
    public string? ExpYear { get; set; }

    public string? BillingNameEncrypted { get; set; }

    public string? ProviderPaymentMethodIdEncrypted { get; set; }

    public bool IsDefault { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
