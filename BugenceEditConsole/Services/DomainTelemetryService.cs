using BugenceEditConsole.Data;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BugenceEditConsole.Services;

public interface IDomainTelemetryService
{
    Task<DomainTelemetrySnapshot> GetSnapshotAsync(int? lookbackHours = null, CancellationToken cancellationToken = default);
}

public class DomainTelemetryService : IDomainTelemetryService
{
    private readonly ApplicationDbContext _db;
    private readonly DomainObservabilityOptions _options;

    public DomainTelemetryService(ApplicationDbContext db, IOptions<DomainObservabilityOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<DomainTelemetrySnapshot> GetSnapshotAsync(int? lookbackHours = null, CancellationToken cancellationToken = default)
    {
        var hours = lookbackHours.HasValue
            ? Math.Clamp(lookbackHours.Value, 1, 168)
            : _options.TelemetryWindowHours;
        hours = Math.Max(1, hours);

        var windowEnd = DateTime.UtcNow;
        var windowStart = windowEnd - TimeSpan.FromHours(hours);

        var totals = await LoadTotalsAsync(cancellationToken);
        var buckets = await LoadBucketsAsync(windowStart, windowEnd, cancellationToken);
        var failures = await LoadFailureInsightsAsync(cancellationToken);
        var latest = await LoadLatestChecksAsync(cancellationToken);

        return new DomainTelemetrySnapshot
        {
            WindowStartUtc = windowStart,
            WindowEndUtc = windowEnd,
            Totals = totals,
            VerificationActivity = buckets,
            FailureInsights = failures,
            LatestChecks = latest
        };
    }

    private async Task<DomainTelemetryTotals> LoadTotalsAsync(CancellationToken cancellationToken)
    {
        var totals = await _db.ProjectDomains.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new DomainTelemetryTotals
            {
                Total = g.Count(),
                Pending = g.Count(d => d.Status == DomainStatus.Pending),
                Verifying = g.Count(d => d.Status == DomainStatus.Verifying),
                Connected = g.Count(d => d.Status == DomainStatus.Connected),
                Failed = g.Count(d => d.Status == DomainStatus.Failed),
                SslActive = g.Count(d => d.SslStatus == DomainSslStatus.Active),
                SslPending = g.Count(d => d.SslStatus == DomainSslStatus.Pending || d.SslStatus == DomainSslStatus.Provisioning),
                SslError = g.Count(d => d.SslStatus == DomainSslStatus.Error)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return totals ?? new DomainTelemetryTotals();
    }

    private async Task<IReadOnlyList<DomainVerificationBucket>> LoadBucketsAsync(DateTime windowStart, DateTime windowEnd, CancellationToken cancellationToken)
    {
        var bucketMinutes = Math.Max(5, _options.TelemetryBucketMinutes);
        var entries = await _db.DomainVerificationLogs.AsNoTracking()
            .Where(l => l.CheckedAtUtc >= windowStart)
            .Select(l => new { l.CheckedAtUtc, l.AllRecordsSatisfied })
            .ToListAsync(cancellationToken);

        var grouped = entries
            .GroupBy(entry => TruncateToBucket(entry.CheckedAtUtc, bucketMinutes))
            .ToDictionary(
                g => g.Key,
                g => new DomainVerificationBucket
                {
                    BucketStartUtc = g.Key,
                    Checks = g.Count(),
                    Successes = g.Count(x => x.AllRecordsSatisfied),
                    Failures = g.Count(x => !x.AllRecordsSatisfied)
                });

        var cursor = TruncateToBucket(windowStart, bucketMinutes);
        var end = TruncateToBucket(windowEnd, bucketMinutes);
        var buckets = new List<DomainVerificationBucket>();
        while (cursor <= end)
        {
            if (grouped.TryGetValue(cursor, out var bucket))
            {
                buckets.Add(bucket);
            }
            else
            {
                buckets.Add(new DomainVerificationBucket
                {
                    BucketStartUtc = cursor,
                    Checks = 0,
                    Successes = 0,
                    Failures = 0
                });
            }
            cursor = cursor.AddMinutes(bucketMinutes);
        }

        return buckets;
    }

    private async Task<IReadOnlyList<DomainFailureInsight>> LoadFailureInsightsAsync(CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, _options.FailureInsightsLimit);
        return await _db.ProjectDomains.AsNoTracking()
            .Include(d => d.Project)
            .Where(d => d.ConsecutiveFailureCount > 0 || d.Status != DomainStatus.Connected)
            .OrderByDescending(d => d.ConsecutiveFailureCount)
            .ThenByDescending(d => d.LastCheckedAtUtc)
            .Take(limit)
            .Select(d => new DomainFailureInsight
            {
                DomainId = d.Id,
                ProjectId = d.UploadedProjectId,
                Domain = d.DomainName,
                Project = d.Project.DisplayName ?? d.Project.FolderName ?? $"Project #{d.Project.Id}",
                FailureStreak = d.ConsecutiveFailureCount,
                Status = d.Status,
                SslStatus = d.SslStatus,
                LastCheckedAtUtc = d.LastCheckedAtUtc,
                LastFailureNotifiedAtUtc = d.LastFailureNotifiedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<DomainVerificationEvent>> LoadLatestChecksAsync(CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, _options.LatestHistoryLimit);
        return await _db.DomainVerificationLogs.AsNoTracking()
            .Include(l => l.Domain)
            .ThenInclude(d => d.Project)
            .OrderByDescending(l => l.CheckedAtUtc)
            .Take(limit)
            .Select(l => new DomainVerificationEvent
            {
                DomainId = l.ProjectDomainId,
                ProjectId = l.Domain.UploadedProjectId,
                Domain = l.Domain.DomainName,
                Project = l.Domain.Project.DisplayName ?? l.Domain.Project.FolderName ?? $"Project #{l.Domain.Project.Id}",
                Status = l.Status.ToString(),
                SslStatus = l.SslStatus.ToString(),
                RecordsSatisfied = l.AllRecordsSatisfied,
                FailureStreak = l.FailureStreak,
                NotificationSent = l.NotificationSent,
                Message = l.Message,
                CheckedAtUtc = l.CheckedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    private static DateTime TruncateToBucket(DateTime value, int bucketMinutes)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        var minute = utc.Minute - (utc.Minute % bucketMinutes);
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, minute, 0, DateTimeKind.Utc);
    }
}
