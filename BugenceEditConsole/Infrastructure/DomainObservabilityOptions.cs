namespace BugenceEditConsole.Infrastructure;

public class DomainObservabilityOptions
{
    public int FailureNotificationThreshold { get; set; } = 3;

    public TimeSpan FailureNotificationCooldown { get; set; } = TimeSpan.FromHours(6);

    public int TelemetryWindowHours { get; set; } = 24;

    public int TelemetryBucketMinutes { get; set; } = 60;

    public int FailureInsightsLimit { get; set; } = 5;

    public int LatestHistoryLimit { get; set; } = 25;
}
