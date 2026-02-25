using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class WorkflowExecutionLog
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid WorkflowId { get; set; }

    [Required]
    public string OwnerUserId { get; set; } = string.Empty;

    [Required, MaxLength(32)]
    public string Status { get; set; } = "Success";

    [MaxLength(240)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? StepName { get; set; }

    public string? SourceUrl { get; set; }

    public DateTime ExecutedAtUtc { get; set; } = DateTime.UtcNow;
}
