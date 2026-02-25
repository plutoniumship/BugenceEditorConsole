using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public class Workflow
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int DisplayId { get; set; }

    [Required, MaxLength(64)]
    public string Dguid { get; set; } = Guid.NewGuid().ToString("N");

    [Required]
    public string OwnerUserId { get; set; } = string.Empty;

    public Guid? CompanyId { get; set; }

    [Required, MaxLength(180)]
    public string CreatedByName { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(180)]
    public string? Caption { get; set; }

    [MaxLength(320)]
    public string? Description { get; set; }

    [Required, MaxLength(40)]
    public string Status { get; set; } = "Draft";

    [Required, MaxLength(40)]
    public string TriggerType { get; set; } = "Manual";

    [MaxLength(80)]
    public string WorkflowType { get; set; } = "Application Workflow";

    [MaxLength(180)]
    public string? ApplicationId { get; set; }

    public int FileListId { get; set; }

    public int ViewOnApplication { get; set; }

    public int StartupType { get; set; } = 1;

    [MaxLength(280)]
    public string? StartupArgument1 { get; set; }

    [MaxLength(280)]
    public string? StartupArgument2 { get; set; }

    [MaxLength(512)]
    public string? Diagram { get; set; }

    [MaxLength(180)]
    public string? KpiActivity { get; set; }

    public bool InActive { get; set; }

    [MaxLength(512)]
    public string? AttachmentPath { get; set; }

    public string? TriggerConfigJson { get; set; }

    public string DefinitionJson { get; set; } = "{}";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
