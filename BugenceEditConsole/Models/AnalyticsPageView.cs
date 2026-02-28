using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class AnalyticsPageView
{
    public long Id { get; set; }

    [Required, MaxLength(128)]
    public string SessionId { get; set; } = string.Empty;

    public int ProjectId { get; set; }

    [Required, MaxLength(253)]
    public string Host { get; set; } = string.Empty;

    [Required, MaxLength(1024)]
    public string Path { get; set; } = "/";

    [MaxLength(1024)]
    public string? PageTitle { get; set; }

    [MaxLength(1024)]
    public string? LandingPath { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    public int? DurationMs { get; set; }

    public int? EngagementTimeMs { get; set; }

    public bool IsBot { get; set; }

    [MaxLength(450)]
    public string? OwnerUserId { get; set; }

    public Guid? CompanyId { get; set; }
}
