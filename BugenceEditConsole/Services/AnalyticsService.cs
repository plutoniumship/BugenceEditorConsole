using System.Security.Cryptography;
using System.Text;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public sealed record AnalyticsIngestContext(
    int ProjectId,
    string Host,
    string Path,
    string SessionId,
    string CountryCode,
    string? DeviceType,
    string? ReferrerHost,
    string? UserAgent,
    bool IsBot,
    DateTime OccurredAtUtc,
    string? OwnerUserId,
    Guid? CompanyId);

public sealed record AnalyticsEventIngestContext(
    int ProjectId,
    string SessionId,
    string EventType,
    string EventName,
    string Path,
    string CountryCode,
    string? DeviceType,
    string? ReferrerHost,
    string? MetadataJson,
    DateTime OccurredAtUtc,
    string? OwnerUserId,
    Guid? CompanyId);

public sealed record AnalyticsTopPage(string Path, int Visits, double Percent);
public sealed record AnalyticsReferrerSummary(string Host, int Visits, double Percent);
public sealed record AnalyticsCountrySummary(string CountryCode, int Visits, double Percent);
public sealed record AnalyticsSourceMix(int NaPercent, int EuPercent, int AsiaPercent);
public sealed record AnalyticsDeviceSummary(int Desktop, int Mobile, int Tablet, int Other);
public sealed record AnalyticsDeviceConversionSummary(string DeviceType, int Conversions, double Percent);
public sealed record AnalyticsFunnelStep(string Key, string Label, int Sessions, int DropoffCount, double DropoffPercent, double StepToNextPercent);
public sealed record AnalyticsFunnelSnapshot(IReadOnlyList<AnalyticsFunnelStep> Steps, int ConversionSessions, double OverallConversionRatePercent);

public sealed record AnalyticsWindowMetrics(
    int UniqueVisitors,
    int Pageviews,
    int Sessions,
    TimeSpan AvgSession,
    double BounceRatePercent);

public sealed record AnalyticsSnapshot(
    int ProjectId,
    string Range,
    DateTime GeneratedAtUtc,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    AnalyticsWindowMetrics Current,
    AnalyticsWindowMetrics Previous,
    int ActiveUsersNow,
    int PageviewsPerMinute,
    DateTime? LastEventSeenUtc,
    IReadOnlyList<double> Traffic24h,
    IReadOnlyList<double> Traffic7d,
    IReadOnlyList<double> Traffic30d,
    IReadOnlyList<AnalyticsTopPage> TopPages,
    IReadOnlyList<AnalyticsReferrerSummary> TopReferrers,
    IReadOnlyList<AnalyticsCountrySummary> TopCountries,
    int ConversionCount,
    double ConversionRatePercent,
    double LeadsPer100Sessions,
    int PreviousConversionCount,
    double PreviousConversionRatePercent,
    double PreviousLeadsPer100Sessions,
    AnalyticsFunnelSnapshot Funnel,
    IReadOnlyList<AnalyticsReferrerSummary> ConversionByReferrers,
    IReadOnlyList<AnalyticsCountrySummary> ConversionByCountries,
    IReadOnlyList<AnalyticsDeviceConversionSummary> ConversionByDevices,
    AnalyticsDeviceSummary Devices,
    AnalyticsSourceMix Sources)
{
    public static AnalyticsSnapshot Empty(int projectId, string range) => new(
        ProjectId: projectId,
        Range: range,
        GeneratedAtUtc: DateTime.UtcNow,
        WindowStartUtc: DateTime.UtcNow,
        WindowEndUtc: DateTime.UtcNow,
        Current: new AnalyticsWindowMetrics(0, 0, 0, TimeSpan.Zero, 0),
        Previous: new AnalyticsWindowMetrics(0, 0, 0, TimeSpan.Zero, 0),
        ActiveUsersNow: 0,
        PageviewsPerMinute: 0,
        LastEventSeenUtc: null,
        Traffic24h: Array.Empty<double>(),
        Traffic7d: Array.Empty<double>(),
        Traffic30d: Array.Empty<double>(),
        TopPages: Array.Empty<AnalyticsTopPage>(),
        TopReferrers: Array.Empty<AnalyticsReferrerSummary>(),
        TopCountries: Array.Empty<AnalyticsCountrySummary>(),
        ConversionCount: 0,
        ConversionRatePercent: 0,
        LeadsPer100Sessions: 0,
        PreviousConversionCount: 0,
        PreviousConversionRatePercent: 0,
        PreviousLeadsPer100Sessions: 0,
        Funnel: new AnalyticsFunnelSnapshot(Array.Empty<AnalyticsFunnelStep>(), 0, 0),
        ConversionByReferrers: Array.Empty<AnalyticsReferrerSummary>(),
        ConversionByCountries: Array.Empty<AnalyticsCountrySummary>(),
        ConversionByDevices: Array.Empty<AnalyticsDeviceConversionSummary>(),
        Devices: new AnalyticsDeviceSummary(0, 0, 0, 0),
        Sources: new AnalyticsSourceMix(0, 0, 0));
}

public interface IAnalyticsIngestService
{
    Task TrackPageViewAsync(AnalyticsIngestContext input, CancellationToken cancellationToken = default);
    Task TrackEventAsync(AnalyticsEventIngestContext input, CancellationToken cancellationToken = default);
}

public interface IAnalyticsQueryService
{
    Task<AnalyticsSnapshot> GetProjectSnapshotAsync(
        string ownerUserId,
        Guid? companyId,
        int projectId,
        string? range,
        CancellationToken cancellationToken = default);

    Task<AnalyticsSnapshot> GetProjectFunnelSnapshotAsync(
        string ownerUserId,
        Guid? companyId,
        int projectId,
        string? range,
        CancellationToken cancellationToken = default);

    Task<AnalyticsSnapshot> GetSnapshotAsync(string ownerUserId, Guid? companyId, CancellationToken cancellationToken = default);
}

public class AnalyticsService : IAnalyticsIngestService, IAnalyticsQueryService
{
    private static readonly SemaphoreSlim EnsureLock = new(1, 1);
    private static bool _ensured;

    private readonly ApplicationDbContext _db;
    private readonly ILogger<AnalyticsService> _logger;

    public AnalyticsService(ApplicationDbContext db, ILogger<AnalyticsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task TrackPageViewAsync(AnalyticsIngestContext input, CancellationToken cancellationToken = default)
    {
        if (input.ProjectId <= 0 || string.IsNullOrWhiteSpace(input.SessionId) || string.IsNullOrWhiteSpace(input.Host))
        {
            return;
        }

        try
        {
            await EnsureTablesAsync(cancellationToken);

            var now = input.OccurredAtUtc == default ? DateTime.UtcNow : input.OccurredAtUtc;
            var normalizedPath = NormalizePath(input.Path);
            var sessionId = input.SessionId.Trim();
            var country = NormalizeCountry(input.CountryCode);
            var deviceType = NormalizeDeviceType(input.DeviceType, input.UserAgent);
            var userAgentHash = Hash(input.UserAgent ?? string.Empty);

            var session = await _db.AnalyticsSessions.FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);
            if (session == null)
            {
                session = new AnalyticsSession
                {
                    SessionId = sessionId,
                    ProjectId = input.ProjectId,
                    Host = input.Host.Trim().ToLowerInvariant(),
                    FirstSeenUtc = now,
                    LastSeenUtc = now,
                    CountryCode = country,
                    DeviceType = deviceType,
                    ReferrerHost = NormalizeHost(input.ReferrerHost),
                    UserAgentHash = userAgentHash,
                    OwnerUserId = input.OwnerUserId,
                    CompanyId = input.CompanyId
                };
                _db.AnalyticsSessions.Add(session);
            }
            else
            {
                session.LastSeenUtc = now;
                if (!string.IsNullOrWhiteSpace(country))
                {
                    session.CountryCode = country;
                }
                if (!string.IsNullOrWhiteSpace(deviceType))
                {
                    session.DeviceType = deviceType;
                }
                if (!string.IsNullOrWhiteSpace(input.OwnerUserId))
                {
                    session.OwnerUserId = input.OwnerUserId;
                }
                if (input.CompanyId.HasValue)
                {
                    session.CompanyId = input.CompanyId;
                }
                if (string.IsNullOrWhiteSpace(session.ReferrerHost))
                {
                    session.ReferrerHost = NormalizeHost(input.ReferrerHost);
                }
            }

            _db.AnalyticsPageViews.Add(new AnalyticsPageView
            {
                SessionId = sessionId,
                ProjectId = input.ProjectId,
                Host = input.Host.Trim().ToLowerInvariant(),
                Path = normalizedPath,
                OccurredAtUtc = now,
                IsBot = input.IsBot,
                DurationMs = null,
                OwnerUserId = input.OwnerUserId,
                CompanyId = input.CompanyId
            });

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Analytics ingest failed for project {ProjectId} host {Host}.", input.ProjectId, input.Host);
        }
    }

    public async Task TrackEventAsync(AnalyticsEventIngestContext input, CancellationToken cancellationToken = default)
    {
        if (input.ProjectId <= 0 ||
            string.IsNullOrWhiteSpace(input.SessionId) ||
            string.IsNullOrWhiteSpace(input.EventType) ||
            string.IsNullOrWhiteSpace(input.EventName))
        {
            return;
        }

        try
        {
            await EnsureTablesAsync(cancellationToken);

            var now = input.OccurredAtUtc == default ? DateTime.UtcNow : input.OccurredAtUtc;
            var eventType = input.EventType.Trim().ToLowerInvariant();
            var eventName = input.EventName.Trim().ToLowerInvariant();
            var sessionId = input.SessionId.Trim();
            var path = NormalizePath(input.Path);

            // 30s dedup window for repeated submit retries from the same session and page.
            var dedupStart = now.AddSeconds(-30);
            var duplicateExists = await _db.AnalyticsEvents.AsNoTracking().AnyAsync(e =>
                e.ProjectId == input.ProjectId &&
                e.SessionId == sessionId &&
                e.EventType == eventType &&
                e.EventName == eventName &&
                e.Path == path &&
                e.OccurredAtUtc >= dedupStart, cancellationToken);
            if (duplicateExists)
            {
                return;
            }

            _db.AnalyticsEvents.Add(new AnalyticsEvent
            {
                ProjectId = input.ProjectId,
                SessionId = sessionId,
                EventType = eventType,
                EventName = eventName,
                Path = path,
                OccurredAtUtc = now,
                CountryCode = NormalizeCountry(input.CountryCode),
                DeviceType = NormalizeDeviceType(input.DeviceType, null),
                ReferrerHost = NormalizeHost(input.ReferrerHost),
                MetadataJson = NormalizeMetadata(input.MetadataJson),
                OwnerUserId = input.OwnerUserId,
                CompanyId = input.CompanyId
            });

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Analytics event ingest failed for project {ProjectId} event {EventName}.", input.ProjectId, input.EventName);
        }
    }

    public async Task<AnalyticsSnapshot> GetProjectSnapshotAsync(
        string ownerUserId,
        Guid? companyId,
        int projectId,
        string? range,
        CancellationToken cancellationToken = default)
    {
        await EnsureTablesAsync(cancellationToken);

        if (projectId <= 0)
        {
            return AnalyticsSnapshot.Empty(projectId, NormalizeRange(range));
        }

        var normalizedRange = NormalizeRange(range);
        var now = DateTime.UtcNow;
        var rangeDuration = normalizedRange switch
        {
            "24h" => TimeSpan.FromHours(24),
            "7d" => TimeSpan.FromDays(7),
            _ => TimeSpan.FromDays(30)
        };

        var currentStart = now - rangeDuration;
        var previousStart = currentStart - rangeDuration;
        var lookbackStart = now.AddDays(-60);

        var viewsQuery = _db.AnalyticsPageViews.AsNoTracking()
            .Where(v => v.ProjectId == projectId);
        if (companyId.HasValue)
        {
            viewsQuery = viewsQuery.Where(v => v.CompanyId == companyId.Value);
        }
        else
        {
            viewsQuery = viewsQuery.Where(v => v.OwnerUserId == ownerUserId);
        }

        var views = await viewsQuery
            .Where(v => !v.IsBot && v.OccurredAtUtc >= lookbackStart)
            .ToListAsync(cancellationToken);

        var sessionsQuery = _db.AnalyticsSessions.AsNoTracking()
            .Where(s => s.ProjectId == projectId);
        if (companyId.HasValue)
        {
            sessionsQuery = sessionsQuery.Where(s => s.CompanyId == companyId.Value);
        }
        else
        {
            sessionsQuery = sessionsQuery.Where(s => s.OwnerUserId == ownerUserId);
        }

        var sessions = await sessionsQuery
            .Where(s => s.LastSeenUtc >= lookbackStart)
            .ToListAsync(cancellationToken);

        var eventsQuery = _db.AnalyticsEvents.AsNoTracking()
            .Where(e => e.ProjectId == projectId);
        if (companyId.HasValue)
        {
            eventsQuery = eventsQuery.Where(e => e.CompanyId == companyId.Value);
        }
        else
        {
            eventsQuery = eventsQuery.Where(e => e.OwnerUserId == ownerUserId);
        }

        var events = await eventsQuery
            .Where(e => e.OccurredAtUtc >= lookbackStart)
            .ToListAsync(cancellationToken);

        var sessionsById = sessions
            .GroupBy(s => s.SessionId)
            .Select(g => g.OrderByDescending(x => x.LastSeenUtc).First())
            .ToDictionary(s => s.SessionId, StringComparer.Ordinal);

        var currentViews = views.Where(v => v.OccurredAtUtc >= currentStart && v.OccurredAtUtc <= now).ToList();
        var previousViews = views.Where(v => v.OccurredAtUtc >= previousStart && v.OccurredAtUtc < currentStart).ToList();
        var currentEvents = events.Where(e => e.OccurredAtUtc >= currentStart && e.OccurredAtUtc <= now).ToList();
        var previousEvents = events.Where(e => e.OccurredAtUtc >= previousStart && e.OccurredAtUtc < currentStart).ToList();

        var currentMetrics = BuildWindowMetrics(currentViews, sessionsById);
        var previousMetrics = BuildWindowMetrics(previousViews, sessionsById);

        var traffic24 = BuildHourlySeries(views.Where(v => v.OccurredAtUtc >= now.AddHours(-24)).Select(v => v.OccurredAtUtc), 24, TimeSpan.FromHours(1), now);
        var traffic7 = BuildDailySeries(views.Where(v => v.OccurredAtUtc >= now.AddDays(-7)).Select(v => v.OccurredAtUtc), 7, now);
        var traffic30 = BuildDailySeries(views.Where(v => v.OccurredAtUtc >= now.AddDays(-30)).Select(v => v.OccurredAtUtc), 30, now);

        var topPages = BuildTopPages(currentViews, 8);

        var currentSessionIds = currentViews
            .Select(v => v.SessionId)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var sessionWindow = currentSessionIds
            .Select(id => sessionsById.TryGetValue(id, out var found) ? found : null)
            .Where(s => s != null)
            .Cast<AnalyticsSession>()
            .ToList();

        var topReferrers = BuildTopReferrers(sessionWindow, 8);
        var topCountries = BuildTopCountries(sessionWindow, 8);
        var devices = BuildDeviceSummary(sessionWindow);
        var regionMix = BuildSourceMix(sessionWindow);

        var currentConversionEvents = currentEvents
            .Where(e => IsPrimaryConversionEvent(e.EventType, e.EventName))
            .ToList();
        var previousConversionEvents = previousEvents
            .Where(e => IsPrimaryConversionEvent(e.EventType, e.EventName))
            .ToList();
        var currentConvertedSessions = currentConversionEvents
            .Select(e => e.SessionId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var previousConvertedSessions = previousConversionEvents
            .Select(e => e.SessionId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var conversionRate = currentMetrics.Sessions <= 0 ? 0 : (currentConvertedSessions * 100.0 / currentMetrics.Sessions);
        var previousConversionRate = previousMetrics.Sessions <= 0 ? 0 : (previousConvertedSessions * 100.0 / previousMetrics.Sessions);
        var leadsPer100 = currentMetrics.Sessions <= 0 ? 0 : (currentConversionEvents.Count * 100.0 / currentMetrics.Sessions);
        var previousLeadsPer100 = previousMetrics.Sessions <= 0 ? 0 : (previousConversionEvents.Count * 100.0 / previousMetrics.Sessions);
        var conversionByReferrers = BuildConversionReferrers(currentConversionEvents, sessionsById, 8);
        var conversionByCountries = BuildConversionCountries(currentConversionEvents, sessionsById, 8);
        var conversionByDevices = BuildConversionDevices(currentConversionEvents, sessionsById);
        var funnel = BuildFunnel(currentViews, currentConversionEvents);

        var activeUsers = sessions.Count(s => s.LastSeenUtc >= now.AddMinutes(-5));
        var pageviewsPerMinute = views.Count(v => v.OccurredAtUtc >= now.AddMinutes(-1));
        var lastEventSeen = views.Count == 0 ? (DateTime?)null : views.Max(v => v.OccurredAtUtc);

        return new AnalyticsSnapshot(
            ProjectId: projectId,
            Range: normalizedRange,
            GeneratedAtUtc: now,
            WindowStartUtc: currentStart,
            WindowEndUtc: now,
            Current: currentMetrics,
            Previous: previousMetrics,
            ActiveUsersNow: activeUsers,
            PageviewsPerMinute: pageviewsPerMinute,
            LastEventSeenUtc: lastEventSeen,
            Traffic24h: traffic24,
            Traffic7d: traffic7,
            Traffic30d: traffic30,
            TopPages: topPages,
            TopReferrers: topReferrers,
            TopCountries: topCountries,
            ConversionCount: currentConversionEvents.Count,
            ConversionRatePercent: conversionRate,
            LeadsPer100Sessions: leadsPer100,
            PreviousConversionCount: previousConversionEvents.Count,
            PreviousConversionRatePercent: previousConversionRate,
            PreviousLeadsPer100Sessions: previousLeadsPer100,
            Funnel: funnel,
            ConversionByReferrers: conversionByReferrers,
            ConversionByCountries: conversionByCountries,
            ConversionByDevices: conversionByDevices,
            Devices: devices,
            Sources: regionMix);
    }

    public async Task<AnalyticsSnapshot> GetSnapshotAsync(string ownerUserId, Guid? companyId, CancellationToken cancellationToken = default)
    {
        var projectId = await _db.UploadedProjects.AsNoTracking()
            .Where(p => companyId.HasValue ? p.CompanyId == companyId : p.UserId == ownerUserId)
            .OrderByDescending(p => p.LastPublishedAtUtc ?? p.UploadedAtUtc)
            .Select(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (projectId <= 0)
        {
            return AnalyticsSnapshot.Empty(0, "30d");
        }

        return await GetProjectSnapshotAsync(ownerUserId, companyId, projectId, "30d", cancellationToken);
    }

    public Task<AnalyticsSnapshot> GetProjectFunnelSnapshotAsync(
        string ownerUserId,
        Guid? companyId,
        int projectId,
        string? range,
        CancellationToken cancellationToken = default)
    {
        return GetProjectSnapshotAsync(ownerUserId, companyId, projectId, range, cancellationToken);
    }

    private static AnalyticsWindowMetrics BuildWindowMetrics(
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyDictionary<string, AnalyticsSession> sessionsById)
    {
        if (views.Count == 0)
        {
            return new AnalyticsWindowMetrics(0, 0, 0, TimeSpan.Zero, 0);
        }

        var sessionGroups = views
            .GroupBy(v => v.SessionId)
            .Select(g => new { SessionId = g.Key, Views = g.Count() })
            .ToList();

        var sessionCount = sessionGroups.Count;
        var singlePageCount = sessionGroups.Count(g => g.Views == 1);

        var avgSeconds = sessionGroups
            .Select(g => sessionsById.TryGetValue(g.SessionId, out var s)
                ? Math.Max(0, (s.LastSeenUtc - s.FirstSeenUtc).TotalSeconds)
                : 0)
            .DefaultIfEmpty(0)
            .Average();

        var bounce = sessionCount <= 0 ? 0 : (singlePageCount * 100.0 / sessionCount);

        return new AnalyticsWindowMetrics(
            UniqueVisitors: sessionCount,
            Pageviews: views.Count,
            Sessions: sessionCount,
            AvgSession: TimeSpan.FromSeconds(avgSeconds),
            BounceRatePercent: bounce);
    }

    private static IReadOnlyList<AnalyticsTopPage> BuildTopPages(IReadOnlyList<AnalyticsPageView> views, int take)
    {
        var grouped = views
            .GroupBy(v => NormalizePath(v.Path))
            .Select(g => new { Path = g.Key, Visits = g.Count() })
            .OrderByDescending(x => x.Visits)
            .Take(take)
            .ToList();

        var max = grouped.Count == 0 ? 1 : grouped.Max(x => x.Visits);
        return grouped
            .Select(x => new AnalyticsTopPage(x.Path, x.Visits, max <= 0 ? 0 : x.Visits * 100.0 / max))
            .ToList();
    }

    private static IReadOnlyList<AnalyticsReferrerSummary> BuildTopReferrers(IReadOnlyList<AnalyticsSession> sessions, int take)
    {
        if (sessions.Count == 0)
        {
            return Array.Empty<AnalyticsReferrerSummary>();
        }

        var grouped = sessions
            .GroupBy(s => string.IsNullOrWhiteSpace(s.ReferrerHost) ? "Direct" : s.ReferrerHost!.Trim().ToLowerInvariant())
            .Select(g => new { Host = g.Key, Visits = g.Count() })
            .OrderByDescending(x => x.Visits)
            .Take(take)
            .ToList();

        var max = grouped.Count == 0 ? 1 : grouped.Max(x => x.Visits);
        return grouped
            .Select(x => new AnalyticsReferrerSummary(x.Host, x.Visits, max <= 0 ? 0 : x.Visits * 100.0 / max))
            .ToList();
    }

    private static IReadOnlyList<AnalyticsCountrySummary> BuildTopCountries(IReadOnlyList<AnalyticsSession> sessions, int take)
    {
        if (sessions.Count == 0)
        {
            return Array.Empty<AnalyticsCountrySummary>();
        }

        var grouped = sessions
            .GroupBy(s => NormalizeCountry(s.CountryCode))
            .Select(g => new { CountryCode = g.Key, Visits = g.Count() })
            .OrderByDescending(x => x.Visits)
            .Take(take)
            .ToList();

        var max = grouped.Count == 0 ? 1 : grouped.Max(x => x.Visits);
        return grouped
            .Select(x => new AnalyticsCountrySummary(x.CountryCode, x.Visits, max <= 0 ? 0 : x.Visits * 100.0 / max))
            .ToList();
    }

    private static bool IsPrimaryConversionEvent(string? eventType, string? eventName)
    {
        return string.Equals(eventType, "form_submit", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(eventName, "contact_form_submit", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<AnalyticsReferrerSummary> BuildConversionReferrers(
        IReadOnlyList<AnalyticsEvent> conversionEvents,
        IReadOnlyDictionary<string, AnalyticsSession> sessionsById,
        int take)
    {
        if (conversionEvents.Count == 0)
        {
            return Array.Empty<AnalyticsReferrerSummary>();
        }

        var grouped = conversionEvents
            .GroupBy(e => sessionsById.TryGetValue(e.SessionId, out var s) && !string.IsNullOrWhiteSpace(s.ReferrerHost)
                ? s.ReferrerHost!.Trim().ToLowerInvariant()
                : "Direct")
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(take)
            .ToList();

        var max = grouped.Count == 0 ? 1 : grouped.Max(x => x.Count);
        return grouped
            .Select(x => new AnalyticsReferrerSummary(x.Key, x.Count, max <= 0 ? 0 : x.Count * 100.0 / max))
            .ToList();
    }

    private static IReadOnlyList<AnalyticsCountrySummary> BuildConversionCountries(
        IReadOnlyList<AnalyticsEvent> conversionEvents,
        IReadOnlyDictionary<string, AnalyticsSession> sessionsById,
        int take)
    {
        if (conversionEvents.Count == 0)
        {
            return Array.Empty<AnalyticsCountrySummary>();
        }

        var grouped = conversionEvents
            .GroupBy(e =>
            {
                if (sessionsById.TryGetValue(e.SessionId, out var s))
                {
                    return NormalizeCountry(s.CountryCode);
                }

                return NormalizeCountry(e.CountryCode);
            })
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(take)
            .ToList();

        var max = grouped.Count == 0 ? 1 : grouped.Max(x => x.Count);
        return grouped
            .Select(x => new AnalyticsCountrySummary(x.Key, x.Count, max <= 0 ? 0 : x.Count * 100.0 / max))
            .ToList();
    }

    private static IReadOnlyList<AnalyticsDeviceConversionSummary> BuildConversionDevices(
        IReadOnlyList<AnalyticsEvent> conversionEvents,
        IReadOnlyDictionary<string, AnalyticsSession> sessionsById)
    {
        if (conversionEvents.Count == 0)
        {
            return Array.Empty<AnalyticsDeviceConversionSummary>();
        }

        var grouped = conversionEvents
            .GroupBy(e =>
            {
                if (sessionsById.TryGetValue(e.SessionId, out var s))
                {
                    return NormalizeDeviceType(s.DeviceType, null);
                }

                return NormalizeDeviceType(e.DeviceType, null);
            })
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        var total = Math.Max(1, grouped.Sum(x => x.Count));
        return grouped
            .Select(x => new AnalyticsDeviceConversionSummary(x.Key, x.Count, x.Count * 100.0 / total))
            .ToList();
    }

    private static AnalyticsFunnelSnapshot BuildFunnel(
        IReadOnlyList<AnalyticsPageView> currentViews,
        IReadOnlyList<AnalyticsEvent> currentConversionEvents)
    {
        var sessions = currentViews
            .Select(v => v.SessionId)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var step1 = sessions.Count;

        var serviceSessions = currentViews
            .Where(v => IsServiceOrSolutionPath(v.Path))
            .Select(v => v.SessionId)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var step2 = serviceSessions.Count;

        var contactPageSessions = currentViews
            .Where(v => IsContactPath(v.Path))
            .Select(v => v.SessionId)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var step3 = contactPageSessions.Count;

        var conversionSessions = currentConversionEvents
            .Select(e => e.SessionId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var step4 = conversionSessions;

        var counts = new[] { step1, step2, step3, step4 };
        var labels = new[]
        {
            ("landing", "Landing Page View"),
            ("service", "Services/Solution View"),
            ("contact", "Contact Page View"),
            ("convert", "Contact Form Submit")
        };

        var steps = new List<AnalyticsFunnelStep>(4);
        for (var i = 0; i < counts.Length; i++)
        {
            var current = counts[i];
            var next = i < counts.Length - 1 ? counts[i + 1] : current;
            var dropoffCount = Math.Max(0, current - next);
            var dropoffPercent = current <= 0 ? 0 : (dropoffCount * 100.0 / current);
            var stepToNext = i < counts.Length - 1
                ? (current <= 0 ? 0 : (next * 100.0 / current))
                : 100;
            steps.Add(new AnalyticsFunnelStep(labels[i].Item1, labels[i].Item2, current, dropoffCount, dropoffPercent, stepToNext));
        }

        var overallRate = step1 <= 0 ? 0 : (step4 * 100.0 / step1);
        return new AnalyticsFunnelSnapshot(steps, step4, overallRate);
    }

    private async Task EnsureTablesAsync(CancellationToken cancellationToken)
    {
        if (_ensured)
        {
            return;
        }

        await EnsureLock.WaitAsync(cancellationToken);
        try
        {
            if (_ensured)
            {
                return;
            }

            var provider = _db.Database.ProviderName ?? string.Empty;
            if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS AnalyticsSessions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId TEXT NOT NULL,
    ProjectId INTEGER NOT NULL,
    Host TEXT NOT NULL,
    FirstSeenUtc TEXT NOT NULL,
    LastSeenUtc TEXT NOT NULL,
    CountryCode TEXT NOT NULL DEFAULT 'UNK',
    DeviceType TEXT NULL,
    UserAgentHash TEXT NOT NULL,
    ReferrerHost TEXT NULL,
    OwnerUserId TEXT NULL,
    CompanyId TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_AnalyticsSessions_SessionId ON AnalyticsSessions(SessionId);
CREATE INDEX IF NOT EXISTS IX_AnalyticsSessions_Scope ON AnalyticsSessions(OwnerUserId, CompanyId, ProjectId, LastSeenUtc);

CREATE TABLE IF NOT EXISTS AnalyticsPageViews (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId TEXT NOT NULL,
    ProjectId INTEGER NOT NULL,
    Host TEXT NOT NULL,
    Path TEXT NOT NULL,
    OccurredAtUtc TEXT NOT NULL,
    DurationMs INTEGER NULL,
    IsBot INTEGER NOT NULL DEFAULT 0,
    OwnerUserId TEXT NULL,
    CompanyId TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_AnalyticsPageViews_Scope ON AnalyticsPageViews(OwnerUserId, CompanyId, ProjectId, OccurredAtUtc);
CREATE INDEX IF NOT EXISTS IX_AnalyticsPageViews_Path ON AnalyticsPageViews(ProjectId, Path, OccurredAtUtc);

CREATE TABLE IF NOT EXISTS AnalyticsEvents (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId TEXT NOT NULL,
    ProjectId INTEGER NOT NULL,
    EventType TEXT NOT NULL,
    EventName TEXT NOT NULL,
    Path TEXT NOT NULL,
    OccurredAtUtc TEXT NOT NULL,
    CountryCode TEXT NOT NULL DEFAULT 'UNK',
    DeviceType TEXT NULL,
    ReferrerHost TEXT NULL,
    MetadataJson TEXT NULL,
    OwnerUserId TEXT NULL,
    CompanyId TEXT NULL
);
CREATE INDEX IF NOT EXISTS IX_AnalyticsEvents_Scope ON AnalyticsEvents(OwnerUserId, CompanyId, ProjectId, OccurredAtUtc);
CREATE INDEX IF NOT EXISTS IX_AnalyticsEvents_Name ON AnalyticsEvents(ProjectId, EventName, OccurredAtUtc);");

                try
                {
                    await _db.Database.ExecuteSqlRawAsync("ALTER TABLE AnalyticsSessions ADD COLUMN DeviceType TEXT NULL;");
                }
                catch
                {
                    // Column already exists.
                }
            }
            else
            {
                await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='AnalyticsSessions' AND xtype='U')
CREATE TABLE AnalyticsSessions (
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SessionId NVARCHAR(128) NOT NULL,
    ProjectId INT NOT NULL,
    Host NVARCHAR(253) NOT NULL,
    FirstSeenUtc DATETIME2 NOT NULL,
    LastSeenUtc DATETIME2 NOT NULL,
    CountryCode NVARCHAR(8) NOT NULL CONSTRAINT DF_AnalyticsSessions_Country DEFAULT 'UNK',
    DeviceType NVARCHAR(16) NULL,
    UserAgentHash NVARCHAR(128) NOT NULL,
    ReferrerHost NVARCHAR(253) NULL,
    OwnerUserId NVARCHAR(450) NULL,
    CompanyId UNIQUEIDENTIFIER NULL
);
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_AnalyticsSessions_SessionId')
CREATE UNIQUE INDEX IX_AnalyticsSessions_SessionId ON AnalyticsSessions(SessionId);
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_AnalyticsSessions_Scope')
CREATE INDEX IX_AnalyticsSessions_Scope ON AnalyticsSessions(OwnerUserId, CompanyId, ProjectId, LastSeenUtc);

IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='AnalyticsPageViews' AND xtype='U')
CREATE TABLE AnalyticsPageViews (
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SessionId NVARCHAR(128) NOT NULL,
    ProjectId INT NOT NULL,
    Host NVARCHAR(253) NOT NULL,
    Path NVARCHAR(1024) NOT NULL,
    OccurredAtUtc DATETIME2 NOT NULL,
    DurationMs INT NULL,
    IsBot BIT NOT NULL DEFAULT 0,
    OwnerUserId NVARCHAR(450) NULL,
    CompanyId UNIQUEIDENTIFIER NULL
);
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_AnalyticsPageViews_Scope')
CREATE INDEX IX_AnalyticsPageViews_Scope ON AnalyticsPageViews(OwnerUserId, CompanyId, ProjectId, OccurredAtUtc);
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_AnalyticsPageViews_Path')
CREATE INDEX IX_AnalyticsPageViews_Path ON AnalyticsPageViews(ProjectId, Path, OccurredAtUtc);

IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='AnalyticsEvents' AND xtype='U')
CREATE TABLE AnalyticsEvents (
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SessionId NVARCHAR(128) NOT NULL,
    ProjectId INT NOT NULL,
    EventType NVARCHAR(64) NOT NULL,
    EventName NVARCHAR(128) NOT NULL,
    Path NVARCHAR(1024) NOT NULL,
    OccurredAtUtc DATETIME2 NOT NULL,
    CountryCode NVARCHAR(8) NOT NULL CONSTRAINT DF_AnalyticsEvents_Country DEFAULT 'UNK',
    DeviceType NVARCHAR(16) NULL,
    ReferrerHost NVARCHAR(253) NULL,
    MetadataJson NVARCHAR(MAX) NULL,
    OwnerUserId NVARCHAR(450) NULL,
    CompanyId UNIQUEIDENTIFIER NULL
);
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_AnalyticsEvents_Scope')
CREATE INDEX IX_AnalyticsEvents_Scope ON AnalyticsEvents(OwnerUserId, CompanyId, ProjectId, OccurredAtUtc);
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_AnalyticsEvents_Name')
CREATE INDEX IX_AnalyticsEvents_Name ON AnalyticsEvents(ProjectId, EventName, OccurredAtUtc);");

                await _db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('AnalyticsSessions', 'DeviceType') IS NULL
    ALTER TABLE AnalyticsSessions ADD DeviceType NVARCHAR(16) NULL;");
            }

            _ensured = true;
        }
        finally
        {
            EnsureLock.Release();
        }
    }

    private static IReadOnlyList<double> BuildHourlySeries(IEnumerable<DateTime> timestamps, int points, TimeSpan bucketSize, DateTime nowUtc)
    {
        var series = new double[points];
        var start = nowUtc - TimeSpan.FromTicks(bucketSize.Ticks * points);
        foreach (var ts in timestamps)
        {
            var offset = ts - start;
            if (offset < TimeSpan.Zero)
            {
                continue;
            }

            var bucket = (int)(offset.Ticks / bucketSize.Ticks);
            if (bucket >= 0 && bucket < points)
            {
                series[bucket]++;
            }
        }

        return series;
    }

    private static IReadOnlyList<double> BuildDailySeries(IEnumerable<DateTime> timestamps, int points, DateTime nowUtc)
    {
        var series = new double[points];
        var startDate = nowUtc.Date.AddDays(-points + 1);
        foreach (var ts in timestamps)
        {
            var dayIndex = (int)(ts.Date - startDate).TotalDays;
            if (dayIndex >= 0 && dayIndex < points)
            {
                series[dayIndex]++;
            }
        }

        return series;
    }

    private static AnalyticsSourceMix BuildSourceMix(IReadOnlyCollection<AnalyticsSession> sessions)
    {
        if (sessions.Count == 0)
        {
            return new AnalyticsSourceMix(0, 0, 0);
        }

        var na = sessions.Count(s => IsNorthAmerica(s.CountryCode));
        var eu = sessions.Count(s => IsEurope(s.CountryCode));
        var asia = sessions.Count(s => IsAsia(s.CountryCode));
        var total = Math.Max(1, na + eu + asia);

        return new AnalyticsSourceMix(
            NaPercent: (int)Math.Round(na * 100.0 / total),
            EuPercent: (int)Math.Round(eu * 100.0 / total),
            AsiaPercent: Math.Max(0, 100 - (int)Math.Round(na * 100.0 / total) - (int)Math.Round(eu * 100.0 / total)));
    }

    private static AnalyticsDeviceSummary BuildDeviceSummary(IReadOnlyCollection<AnalyticsSession> sessions)
    {
        if (sessions.Count == 0)
        {
            return new AnalyticsDeviceSummary(0, 0, 0, 0);
        }

        var desktop = 0;
        var mobile = 0;
        var tablet = 0;
        var other = 0;
        foreach (var session in sessions)
        {
            var kind = NormalizeDeviceType(session.DeviceType, null);
            switch (kind)
            {
                case "mobile":
                    mobile++;
                    break;
                case "tablet":
                    tablet++;
                    break;
                case "desktop":
                    desktop++;
                    break;
                default:
                    other++;
                    break;
            }
        }

        return new AnalyticsDeviceSummary(desktop, mobile, tablet, other);
    }

    private static string NormalizeRange(string? range)
    {
        if (string.IsNullOrWhiteSpace(range))
        {
            return "30d";
        }

        var normalized = range.Trim().ToLowerInvariant();
        return normalized is "24h" or "7d" or "30d" ? normalized : "30d";
    }

    private static bool IsNorthAmerica(string? code)
    {
        code = NormalizeCountry(code);
        return code is "US" or "CA" or "MX";
    }

    private static bool IsEurope(string? code)
    {
        code = NormalizeCountry(code);
        return code is "GB" or "DE" or "FR" or "ES" or "IT" or "NL" or "SE" or "NO" or "DK" or "FI" or "IE" or "PL" or "PT" or "CH" or "AT" or "BE";
    }

    private static bool IsAsia(string? code)
    {
        code = NormalizeCountry(code);
        return code is "IN" or "PK" or "BD" or "CN" or "JP" or "KR" or "SG" or "AE" or "SA" or "ID" or "TH" or "MY" or "PH" or "VN";
    }

    private static bool IsServiceOrSolutionPath(string? path)
    {
        var normalized = NormalizePath(path).ToLowerInvariant();
        return normalized.Contains("service", StringComparison.Ordinal) ||
               normalized.Contains("solution", StringComparison.Ordinal);
    }

    private static bool IsContactPath(string? path)
    {
        var normalized = NormalizePath(path).ToLowerInvariant();
        return normalized.Contains("contact", StringComparison.Ordinal) ||
               normalized.Contains("lead", StringComparison.Ordinal) ||
               normalized.Contains("inquiry", StringComparison.Ordinal) ||
               normalized.Contains("enquiry", StringComparison.Ordinal) ||
               normalized.Contains("quote", StringComparison.Ordinal);
    }

    private static string NormalizeCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return "UNK";
        }

        country = country.Trim().ToUpperInvariant();
        if (country.Length == 2 && char.IsLetter(country[0]) && char.IsLetter(country[1]))
        {
            return country;
        }

        if (country.Length >= 5 && country[2] is '-' or '_')
        {
            var region = country.Substring(3, Math.Min(2, country.Length - 3));
            if (region.Length == 2 && char.IsLetter(region[0]) && char.IsLetter(region[1]))
            {
                return region;
            }
        }

        return "UNK";
    }

    private static string NormalizeDeviceType(string? deviceType, string? userAgent)
    {
        if (!string.IsNullOrWhiteSpace(deviceType))
        {
            var normalized = deviceType.Trim().ToLowerInvariant();
            if (normalized is "desktop" or "mobile" or "tablet")
            {
                return normalized;
            }
        }

        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "desktop";
        }

        var ua = userAgent.ToLowerInvariant();
        if (ua.Contains("ipad", StringComparison.Ordinal) || ua.Contains("tablet", StringComparison.Ordinal))
        {
            return "tablet";
        }

        if (ua.Contains("mobi", StringComparison.Ordinal) ||
            ua.Contains("iphone", StringComparison.Ordinal) ||
            ua.Contains("android", StringComparison.Ordinal))
        {
            return "mobile";
        }

        return "desktop";
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var result = path.Trim();
        if (!result.StartsWith('/'))
        {
            result = "/" + result;
        }

        var queryIndex = result.IndexOf('?');
        if (queryIndex >= 0)
        {
            result = result[..queryIndex];
        }

        return result.Length > 1024 ? result[..1024] : result;
    }

    private static string? NormalizeMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        var trimmed = metadata.Trim();
        return trimmed.Length > 2048 ? trimmed[..2048] : trimmed;
    }

    private static string? NormalizeHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var host = value.Trim().ToLowerInvariant();
        if (Uri.TryCreate(host, UriKind.Absolute, out var absolute))
        {
            host = absolute.Host;
        }

        if (host.Length > 253)
        {
            host = host[..253];
        }

        return host;
    }

    private static string Hash(string value)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}
