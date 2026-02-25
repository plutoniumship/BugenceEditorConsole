using System.ComponentModel.DataAnnotations;

namespace BugenceEditConsole.Models;

public enum ProjectDomainType
{
    Primary = 0,
    Custom = 1
}

public enum DomainStatus
{
    Pending = 0,
    Verifying = 1,
    Connected = 2,
    Failed = 3,
    Removing = 4
}

public enum DomainSslStatus
{
    Pending = 0,
    Provisioning = 1,
    Active = 2,
    Error = 3
}

public class ProjectDomain
{
    public Guid Id { get; set; }

    public int UploadedProjectId { get; set; }

    public UploadedProject Project { get; set; } = null!;

    [Required, MaxLength(253)]
    public string DomainName { get; set; } = string.Empty;

    [Required, MaxLength(253)]
    public string NormalizedDomain { get; set; } = string.Empty;

    [MaxLength(253)]
    public string? ApexRoot { get; set; }

    public ProjectDomainType DomainType { get; set; } = ProjectDomainType.Custom;

    public DomainStatus Status { get; set; } = DomainStatus.Pending;

    public DomainSslStatus SslStatus { get; set; } = DomainSslStatus.Pending;

    [MaxLength(128)]
    public string? VerificationToken { get; set; }

    [MaxLength(256)]
    public string? FailureReason { get; set; }

    [MaxLength(512)]
    public string? CertificatePath { get; set; }

    [MaxLength(512)]
    public string? CertificateKeyPath { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? VerifiedAtUtc { get; set; }

    public DateTime? LastCheckedAtUtc { get; set; }

    public DateTime? LastSslRenewalAtUtc { get; set; }

    public int ConsecutiveFailureCount { get; set; }

    public DateTime? LastFailureNotifiedAtUtc { get; set; }

    public ICollection<ProjectDomainDnsRecord> DnsRecords { get; set; } = new List<ProjectDomainDnsRecord>();
}

public class ProjectDomainDnsRecord
{
    public Guid Id { get; set; }

    public Guid ProjectDomainId { get; set; }

    public ProjectDomain Domain { get; set; } = null!;

    [Required, MaxLength(16)]
    public string RecordType { get; set; } = "TXT";

    [Required, MaxLength(253)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(512)]
    public string Value { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Purpose { get; set; } = "ownership";

    public bool IsRequired { get; set; } = true;

    public bool IsSatisfied { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastCheckedAtUtc { get; set; }

    [MaxLength(128)]
    public string? ExternalRecordId { get; set; }
}
