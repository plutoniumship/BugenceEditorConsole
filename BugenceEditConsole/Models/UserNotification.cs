using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class UserNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(600)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Type { get; set; } = "info";

    public bool IsRead { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string? MetadataJson { get; set; }
}
