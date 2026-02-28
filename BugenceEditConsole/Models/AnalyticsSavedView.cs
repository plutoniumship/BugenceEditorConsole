using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class AnalyticsSavedView
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int ProjectId { get; set; }

    [Required, MaxLength(450)]
    public string OwnerUserId { get; set; } = string.Empty;

    public Guid? CompanyId { get; set; }

    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string Tab { get; set; } = "overview";

    [Required, MaxLength(8)]
    public string Range { get; set; } = "30d";

    public bool Compare { get; set; }

    [Required, MaxLength(24)]
    public string Segment { get; set; } = "all";

    [MaxLength(4000)]
    public string FiltersJson { get; set; } = "{}";

    public bool IsSystemPreset { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
