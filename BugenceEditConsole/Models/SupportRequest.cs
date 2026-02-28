using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class SupportRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(32)]
    public string DisplayId { get; set; } = string.Empty;

    [MaxLength(450)]
    public string? OwnerUserId { get; set; }

    [MaxLength(120)]
    public string? RequesterName { get; set; }

    [MaxLength(320)]
    public string? RequesterEmail { get; set; }

    [Required, MaxLength(64)]
    public string Category { get; set; } = "general_support";

    [MaxLength(64)]
    public string? IntegrationKey { get; set; }

    [MaxLength(128)]
    public string? SourcePage { get; set; }

    [Required, MaxLength(180)]
    public string Subject { get; set; } = string.Empty;

    [Required, MaxLength(4000)]
    public string Message { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string Status { get; set; } = "submitted";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
