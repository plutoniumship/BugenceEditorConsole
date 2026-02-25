namespace BugenceEditConsole.Models;

public class DomainTelemetrySnapshot
{
    public DateTime WindowStartUtc { get; init; }

    public DateTime WindowEndUtc { get; init; }

    public DomainTelemetryTotals Totals { get; init; } = new();

    public IReadOnlyList<DomainVerificationBucket> VerificationActivity { get; init; } = Array.Empty<DomainVerificationBucket>();

    public IReadOnlyList<DomainFailureInsight> FailureInsights { get; init; } = Array.Empty<DomainFailureInsight>();

    public IReadOnlyList<DomainVerificationEvent> LatestChecks { get; init; } = Array.Empty<DomainVerificationEvent>();
}

public class DomainTelemetryTotals
{
    public int Total { get; init; }

    public int Pending { get; init; }

    public int Verifying { get; init; }

    public int Connected { get; init; }

    public int Failed { get; init; }

    public int SslActive { get; init; }

    public int SslPending { get; init; }

    public int SslError { get; init; }
}

public class DomainVerificationBucket
{
    public DateTime BucketStartUtc { get; init; }

    public int Checks { get; init; }

    public int Successes { get; init; }

    public int Failures { get; init; }
}

public class DomainFailureInsight
{
    public Guid DomainId { get; init; }

    public int ProjectId { get; init; }

    public string Domain { get; init; } = string.Empty;

    public string Project { get; init; } = string.Empty;

    public int FailureStreak { get; init; }

    public DomainStatus Status { get; init; }

    public DomainSslStatus SslStatus { get; init; }

    public DateTime? LastCheckedAtUtc { get; init; }

    public DateTime? LastFailureNotifiedAtUtc { get; init; }
}

public class DomainVerificationEvent
{
    public Guid DomainId { get; init; }

    public int ProjectId { get; init; }

    public string Domain { get; init; } = string.Empty;

    public string Project { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string SslStatus { get; init; } = string.Empty;

    public bool RecordsSatisfied { get; init; }

    public int FailureStreak { get; init; }

    public bool NotificationSent { get; init; }

    public string? Message { get; init; }

    public DateTime CheckedAtUtc { get; init; }
}
