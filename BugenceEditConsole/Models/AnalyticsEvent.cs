using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class AnalyticsEvent
{
    public long Id { get; set; }

    [Required, MaxLength(128)]
    public string SessionId { get; set; } = string.Empty;

    public int ProjectId { get; set; }

    [Required, MaxLength(64)]
    public string EventType { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string EventName { get; set; } = string.Empty;

    [Required, MaxLength(1024)]
    public string Path { get; set; } = "/";

    [MaxLength(1024)]
    public string? PageTitle { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(8)]
    public string CountryCode { get; set; } = "UNK";

    [MaxLength(16)]
    public string? DeviceType { get; set; }

    [MaxLength(253)]
    public string? ReferrerHost { get; set; }

    [MaxLength(2048)]
    public string? MetadataJson { get; set; }

    [MaxLength(160)]
    public string? UtmSource { get; set; }

    [MaxLength(160)]
    public string? UtmMedium { get; set; }

    [MaxLength(200)]
    public string? UtmCampaign { get; set; }

    [MaxLength(200)]
    public string? UtmTerm { get; set; }

    [MaxLength(200)]
    public string? UtmContent { get; set; }

    [MaxLength(450)]
    public string? OwnerUserId { get; set; }

    public Guid? CompanyId { get; set; }
}
