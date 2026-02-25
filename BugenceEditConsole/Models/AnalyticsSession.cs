using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class AnalyticsSession
{
    public long Id { get; set; }

    [Required, MaxLength(128)]
    public string SessionId { get; set; } = string.Empty;

    public int ProjectId { get; set; }

    [Required, MaxLength(253)]
    public string Host { get; set; } = string.Empty;

    public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(8)]
    public string CountryCode { get; set; } = "UNK";

    [MaxLength(128)]
    public string UserAgentHash { get; set; } = string.Empty;

    [MaxLength(253)]
    public string? ReferrerHost { get; set; }

    [MaxLength(16)]
    public string? DeviceType { get; set; }

    [MaxLength(450)]
    public string? OwnerUserId { get; set; }

    public Guid? CompanyId { get; set; }
}
