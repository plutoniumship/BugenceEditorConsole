using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class DomainVerificationLog
{
    public Guid Id { get; set; }

    public Guid ProjectDomainId { get; set; }

    public ProjectDomain Domain { get; set; } = null!;

    public DomainStatus Status { get; set; }

    public DomainSslStatus SslStatus { get; set; }

    public bool AllRecordsSatisfied { get; set; }

    [MaxLength(512)]
    public string? Message { get; set; }

    public int FailureStreak { get; set; }

    public bool NotificationSent { get; set; }

    public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;
}
