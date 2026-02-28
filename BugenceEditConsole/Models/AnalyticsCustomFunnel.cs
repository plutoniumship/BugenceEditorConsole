using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class AnalyticsCustomFunnel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ProjectId { get; set; }
    [Required, MaxLength(450)] public string OwnerUserId { get; set; } = string.Empty;
    public Guid? CompanyId { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required] public string StepsJson { get; set; } = "[]";
    public bool IsSystemPreset { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

