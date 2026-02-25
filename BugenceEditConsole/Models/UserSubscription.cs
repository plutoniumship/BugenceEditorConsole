using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public enum SubscriptionInterval
{
    Trial = 0,
    Monthly = 1,
    SixMonths = 2,
    Yearly = 3,
    Custom = 9
}

public class UserSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string PlanKey { get; set; } = "Starter";

    [Required, MaxLength(32)]
    public string Status { get; set; } = "Trial";

    public SubscriptionInterval Interval { get; set; } = SubscriptionInterval.Trial;

    public DateTime? CurrentPeriodStartUtc { get; set; }

    public DateTime? CurrentPeriodEndUtc { get; set; }

    public DateTime? TrialEndsUtc { get; set; }

    [MaxLength(32)]
    public string Provider { get; set; } = "None";

    [MaxLength(160)]
    public string? ProviderCustomerId { get; set; }

    [MaxLength(160)]
    public string? ProviderSubscriptionId { get; set; }

    [MaxLength(64)]
    public string? LastPaymentStatus { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
