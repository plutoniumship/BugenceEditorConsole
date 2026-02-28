using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    string? City,
    string? Language,
    string? ReferrerHost,
    string? PageTitle,
    string? LandingPath,
    int? EngagementTimeMs,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    string? UtmTerm,
    string? UtmContent,
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
    string? PageTitle,
    string CountryCode,
    string? DeviceType,
    string? Language,
    string? ReferrerHost,
    string? MetadataJson,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    string? UtmTerm,
    string? UtmContent,
    DateTime OccurredAtUtc,
    string? OwnerUserId,
    Guid? CompanyId);

public sealed record AnalyticsQueryFilters(
    string? Country,
    string? Device,
    string? LandingPage,
    string? Referrer,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign);

public sealed record AnalyticsTabQuery(
    string Tab,
    bool Compare,
    string Segment,
    AnalyticsQueryFilters Filters,
    int Page = 1,
    int PageSize = 25,
    string? Search = null,
    string? SortBy = null,
    string? SortDir = null,
    string? Dimension = null,
    string? Module = null,
    string? FunnelMode = null,
    Guid? FunnelId = null);

public sealed record AnalyticsKeyMetric(string Key, string Label, double Value, double? PreviousValue = null, string? Unit = null);
public sealed record AnalyticsTableColumn(string Key, string Label, string? HelpText = null);
public sealed record AnalyticsTableRow(IReadOnlyDictionary<string, string> Cells);
public sealed record AnalyticsTablePayload(IReadOnlyList<AnalyticsTableColumn> Columns, IReadOnlyList<AnalyticsTableRow> Rows, int TotalRows, int Page, int PageSize);
public sealed record AnalyticsSeriesPoint(string Label, double Value, double? PreviousValue = null);
public sealed record AnalyticsListMetric(string Label, double Value, double Percent = 0);
public sealed record AnalyticsTabPayload(
    string Tab,
    IReadOnlyList<AnalyticsKeyMetric> Metrics,
    IReadOnlyList<AnalyticsSeriesPoint> Series,
    IReadOnlyList<AnalyticsListMetric> Lists,
    AnalyticsTablePayload? Table,
    IReadOnlyDictionary<string, object?> Extras);

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

    Task<AnalyticsTabPayload> GetTabPayloadAsync(
        string ownerUserId,
        Guid? companyId,
        int projectId,
        string? range,
        AnalyticsTabQuery query,
        CancellationToken cancellationToken = default);
}

public class AnalyticsService : IAnalyticsIngestService, IAnalyticsQueryService
{
    private readonly SemaphoreSlim _ensureLock = new(1, 1);
    private bool _ensured;

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
            var browser = NormalizeBrowser(input.UserAgent);
            var os = NormalizeOs(input.UserAgent);
            var channel = ResolveChannel(input.UtmSource, input.UtmMedium, input.ReferrerHost);
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
                    City = NormalizeCity(input.City),
                    Language = NormalizeLanguage(input.Language),
                    Browser = browser,
                    Os = os,
                    Channel = channel,
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
                if (!string.IsNullOrWhiteSpace(input.City))
                {
                    session.City = NormalizeCity(input.City);
                }
                if (!string.IsNullOrWhiteSpace(input.Language))
                {
                    session.Language = NormalizeLanguage(input.Language);
                }
                if (!string.IsNullOrWhiteSpace(browser))
                {
                    session.Browser = browser;
                }
                if (!string.IsNullOrWhiteSpace(os))
                {
                    session.Os = os;
                }
                if (!string.IsNullOrWhiteSpace(channel))
                {
                    session.Channel = channel;
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
                PageTitle = NormalizePageTitle(input.PageTitle),
                LandingPath = NormalizePath(input.LandingPath ?? normalizedPath),
                OccurredAtUtc = now,
                IsBot = input.IsBot,
                DurationMs = null,
                EngagementTimeMs = input.EngagementTimeMs,
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
                PageTitle = NormalizePageTitle(input.PageTitle),
                OccurredAtUtc = now,
                CountryCode = NormalizeCountry(input.CountryCode),
                DeviceType = NormalizeDeviceType(input.DeviceType, null),
                ReferrerHost = NormalizeHost(input.ReferrerHost),
                MetadataJson = NormalizeMetadata(input.MetadataJson),
                UtmSource = NormalizeUtm(input.UtmSource, 160),
                UtmMedium = NormalizeUtm(input.UtmMedium, 160),
                UtmCampaign = NormalizeUtm(input.UtmCampaign, 200),
                UtmTerm = NormalizeUtm(input.UtmTerm, 200),
                UtmContent = NormalizeUtm(input.UtmContent, 200),
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

    public async Task<AnalyticsTabPayload> GetTabPayloadAsync(
        string ownerUserId,
        Guid? companyId,
        int projectId,
        string? range,
        AnalyticsTabQuery query,
        CancellationToken cancellationToken = default)
    {
        await EnsureTablesAsync(cancellationToken);

        var snapshot = await GetProjectSnapshotAsync(ownerUserId, companyId, projectId, range, cancellationToken);
        var normalizedRange = NormalizeRange(range);
        var now = DateTime.UtcNow;
        var duration = normalizedRange switch
        {
            "24h" => TimeSpan.FromHours(24),
            "7d" => TimeSpan.FromDays(7),
            _ => TimeSpan.FromDays(30)
        };
        var start = now - duration;
        var previousStart = start - duration;

        var sessionsQuery = _db.AnalyticsSessions.AsNoTracking().Where(s => s.ProjectId == projectId);
        var viewsQuery = _db.AnalyticsPageViews.AsNoTracking().Where(v => v.ProjectId == projectId);
        var eventsQuery = _db.AnalyticsEvents.AsNoTracking().Where(e => e.ProjectId == projectId);

        if (companyId.HasValue)
        {
            sessionsQuery = sessionsQuery.Where(s => s.CompanyId == companyId.Value);
            viewsQuery = viewsQuery.Where(v => v.CompanyId == companyId.Value);
            eventsQuery = eventsQuery.Where(e => e.CompanyId == companyId.Value);
        }
        else
        {
            sessionsQuery = sessionsQuery.Where(s => s.OwnerUserId == ownerUserId);
            viewsQuery = viewsQuery.Where(v => v.OwnerUserId == ownerUserId);
            eventsQuery = eventsQuery.Where(e => e.OwnerUserId == ownerUserId);
        }

        var sessions = await sessionsQuery.Where(s => s.LastSeenUtc >= previousStart).ToListAsync(cancellationToken);
        var views = await viewsQuery.Where(v => !v.IsBot && v.OccurredAtUtc >= previousStart).ToListAsync(cancellationToken);
        var events = await eventsQuery.Where(e => e.OccurredAtUtc >= previousStart).ToListAsync(cancellationToken);

        var sessionsById = sessions
            .GroupBy(s => s.SessionId, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(x => x.LastSeenUtc).First())
            .ToDictionary(x => x.SessionId, StringComparer.Ordinal);

        var currentViews = views.Where(v => v.OccurredAtUtc >= start && v.OccurredAtUtc <= now).ToList();
        var currentEvents = events.Where(e => e.OccurredAtUtc >= start && e.OccurredAtUtc <= now).ToList();
        var previousViews = views.Where(v => v.OccurredAtUtc >= previousStart && v.OccurredAtUtc < start).ToList();
        var previousEvents = events.Where(e => e.OccurredAtUtc >= previousStart && e.OccurredAtUtc < start).ToList();

        ApplySegmentAndFilters(query, sessionsById, ref currentViews, ref currentEvents);
        ApplySegmentAndFilters(query, sessionsById, ref previousViews, ref previousEvents);

        var tab = NormalizeTab(query.Tab);
        return tab switch
        {
            "realtime" => BuildRealtimeTabPayload(snapshot, currentViews, currentEvents, previousViews, previousEvents, sessionsById, query.Compare),
            "acquisition" => BuildAcquisitionTabPayload(query, currentViews, currentEvents, previousViews, previousEvents, sessionsById),
            "engagement" => BuildEngagementTabPayload(query, currentViews, currentEvents, previousViews, sessionsById),
            "audience" => BuildAudienceTabPayload(query, currentViews, currentEvents, previousViews, previousEvents, sessionsById),
            "conversions" => await BuildConversionsTabPayloadAsync(ownerUserId, companyId, projectId, query, currentViews, previousViews, currentEvents, previousEvents, sessionsById, cancellationToken),
            "seo" => BuildSeoTabPayload(),
            "pages" => BuildPagesTabPayload(query, currentViews, currentEvents, sessionsById),
            _ => BuildOverviewTabPayload(snapshot, currentViews, currentEvents, sessionsById)
        };
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

    private static AnalyticsTabPayload BuildOverviewTabPayload(
        AnalyticsSnapshot snapshot,
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyList<AnalyticsEvent> events,
        IReadOnlyDictionary<string, AnalyticsSession> sessionsById)
    {
        var pageRows = BuildPagesTableRows(views, events, sessionsById, 8);
        var countries = views
            .Select(v => sessionsById.TryGetValue(v.SessionId, out var s) ? NormalizeCountry(s.CountryCode) : "UNK")
            .GroupBy(x => x)
            .Select(g => new AnalyticsListMetric(g.Key, g.Count()))
            .OrderByDescending(x => x.Value)
            .Take(3)
            .ToList();
        return new AnalyticsTabPayload(
            Tab: "overview",
            Metrics: new[]
            {
                new AnalyticsKeyMetric("users","Active users", snapshot.Current.UniqueVisitors, snapshot.Previous.UniqueVisitors),
                new AnalyticsKeyMetric("sessions","Sessions", snapshot.Current.Sessions, snapshot.Previous.Sessions),
                new AnalyticsKeyMetric("conversions","Key events", snapshot.ConversionCount, snapshot.PreviousConversionCount)
            },
            Series: BuildSimpleSeries(snapshot.Range, snapshot.Traffic24h, snapshot.Traffic7d, snapshot.Traffic30d),
            Lists: countries,
            Table: new AnalyticsTablePayload(
                BuildPagesColumns(),
                pageRows.Select(ToTableRow).ToList(),
                pageRows.Count,
                1,
                8),
            Extras: new Dictionary<string, object?>
            {
                ["suggested"] = new
                {
                    activeUsersByCountry = countries,
                    viewsByPage = pageRows.Take(3).Select(x => new { x.Page, x.Views }),
                    sessionsByChannel = BuildChannelMix(views, sessionsById)
                },
                ["contractVersion"] = "v2",
                ["widgets"] = new[] { "suggested_cards", "audience_snapshot", "pages_snapshot" }
            });
    }

    private AnalyticsTabPayload BuildRealtimeTabPayload(
        AnalyticsSnapshot snapshot,
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyList<AnalyticsEvent> events,
        IReadOnlyList<AnalyticsPageView> previousViews,
        IReadOnlyList<AnalyticsEvent> previousEvents,
        IReadOnlyDictionary<string, AnalyticsSession> sessionsById,
        bool compare)
    {
        var liveWindow = DateTime.UtcNow.AddMinutes(-5);
        var liveViews = views.Where(v => v.OccurredAtUtc >= liveWindow).ToList();
        var previousLiveViews = previousViews.Where(v => v.OccurredAtUtc >= liveWindow.AddMinutes(-5)).ToList();
        var liveEvents = events.Where(e => e.OccurredAtUtc >= liveWindow).OrderByDescending(x => x.OccurredAtUtc).Take(20).ToList();
        var previousLiveEvents = previousEvents.Where(e => e.OccurredAtUtc >= liveWindow.AddMinutes(-5)).ToList();
        var topPages = liveViews.GroupBy(v => NormalizePath(v.Path)).Select(g => new AnalyticsListMetric(g.Key, g.Count())).OrderByDescending(x => x.Value).Take(8).ToList();
        var topReferrers = liveViews
            .Select(v => sessionsById.TryGetValue(v.SessionId, out var s) ? (s.ReferrerHost ?? "Direct") : "Direct")
            .GroupBy(x => string.IsNullOrWhiteSpace(x) ? "Direct" : x)
            .Select(g => new AnalyticsListMetric(g.Key, g.Count()))
            .OrderByDescending(x => x.Value)
            .Take(8)
            .ToList();
        var geoDots = liveViews
            .GroupBy(v => sessionsById.TryGetValue(v.SessionId, out var s) ? NormalizeCountry(s.CountryCode) : "UNK")
            .Select(g =>
            {
                var coords = TryMapCountryToLatLon(g.Key);
                return new
                {
                    country = g.Key,
                    activeUsers = g.Select(v => v.SessionId).Distinct(StringComparer.Ordinal).Count(),
                    lat = coords?.lat,
                    lon = coords?.lon
                };
            })
            .OrderByDescending(x => x.activeUsers)
            .Take(20)
            .ToList();

        return new AnalyticsTabPayload(
            Tab: "realtime",
            Metrics: new[]
            {
                new AnalyticsKeyMetric("active_now", "Active users right now", snapshot.ActiveUsersNow, compare ? Math.Max(0, previousLiveViews.Select(v => v.SessionId).Distinct(StringComparer.Ordinal).Count()) : null),
                new AnalyticsKeyMetric("ppm", "Pageviews / minute", snapshot.PageviewsPerMinute, compare ? previousLiveViews.Count : null),
                new AnalyticsKeyMetric("events", "Live events (5m)", liveEvents.Count, compare ? previousLiveEvents.Count : null)
            },
            Series: Array.Empty<AnalyticsSeriesPoint>(),
            Lists: topPages,
            Table: new AnalyticsTablePayload(
                new[]
                {
                    new AnalyticsTableColumn("time","Time"),
                    new AnalyticsTableColumn("event","Event"),
                    new AnalyticsTableColumn("page","Page")
                },
                liveEvents.Select(e => new AnalyticsTableRow(new Dictionary<string, string>
                {
                    ["time"] = e.OccurredAtUtc.ToString("HH:mm:ss"),
                    ["event"] = $"{e.EventType}:{e.EventName}",
                    ["page"] = NormalizePath(e.Path)
                })).ToList(),
                liveEvents.Count,
                1,
                20),
            Extras: new Dictionary<string, object?>
            {
                ["topReferrers"] = topReferrers,
                ["geoDots"] = geoDots,
                ["contractVersion"] = "v2",
                ["widgets"] = new[] { "active_counter", "events_stream", "top_pages", "top_referrers", "geo_dots_map" }
            });
    }

    private static AnalyticsTabPayload BuildAcquisitionTabPayload(
        AnalyticsTabQuery query,
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyList<AnalyticsEvent> events,
        IReadOnlyList<AnalyticsPageView> previousViews,
        IReadOnlyList<AnalyticsEvent> previousEvents,
        IReadOnlyDictionary<string, AnalyticsSession> sessionsById)
    {
        var channelMix = BuildChannelMix(views, sessionsById);
        var module = NormalizeAcquisitionModule(query.Module);
        var sourceMediumRows = BuildSourceMediumRows(views, events);
        var campaignRows = BuildCampaignRows(views, events);
        var landingRows = BuildLandingRows(views, events);
        var sourceFiltered = ApplyTextTableSearchSortPagination(sourceMediumRows, query, "source_medium", "sessions");
        var campaignFiltered = ApplyTextTableSearchSortPagination(campaignRows, query, "campaign", "sessions");
        var landingFiltered = ApplyTextTableSearchSortPagination(landingRows, query, "landing_page", "sessions");

        AnalyticsTablePayload table = module switch
        {
            "campaigns" => new AnalyticsTablePayload(
                new[]
                {
                    new AnalyticsTableColumn("campaign", "Campaign"),
                    new AnalyticsTableColumn("source_medium", "Source / Medium"),
                    new AnalyticsTableColumn("sessions", "Sessions"),
                    new AnalyticsTableColumn("key_events", "Key events")
                },
                campaignFiltered.Rows.Select(r => new AnalyticsTableRow(r)).ToList(),
                campaignFiltered.Total,
                campaignFiltered.Page,
                campaignFiltered.PageSize),
            "landing" => new AnalyticsTablePayload(
                new[]
                {
                    new AnalyticsTableColumn("landing_page", "Landing page"),
                    new AnalyticsTableColumn("sessions", "Sessions"),
                    new AnalyticsTableColumn("conversion_rate", "Conversion rate"),
                    new AnalyticsTableColumn("key_events", "Key events")
                },
                landingFiltered.Rows.Select(r => new AnalyticsTableRow(r)).ToList(),
                landingFiltered.Total,
                landingFiltered.Page,
                landingFiltered.PageSize),
            _ => new AnalyticsTablePayload(
                new[]
                {
                    new AnalyticsTableColumn("source_medium", "Source / Medium"),
                    new AnalyticsTableColumn("sessions", "Sessions"),
                    new AnalyticsTableColumn("users", "Users"),
                    new AnalyticsTableColumn("key_events", "Key events")
                },
                sourceFiltered.Rows.Select(r => new AnalyticsTableRow(r)).ToList(),
                sourceFiltered.Total,
                sourceFiltered.Page,
                sourceFiltered.PageSize)
        };

        var previousChannelMix = BuildChannelMix(previousViews, sessionsById)
            .ToDictionary(x => x.Label, x => x.Value, StringComparer.OrdinalIgnoreCase);

        return new AnalyticsTabPayload(
            Tab: "acquisition",
            Metrics: channelMix.Select(x =>
            {
                previousChannelMix.TryGetValue(x.Label, out var prev);
                return new AnalyticsKeyMetric($"ch_{x.Label}", x.Label, x.Value, query.Compare ? prev : null);
            }).ToList(),
            Series: channelMix
                .Select(x =>
                {
                    previousChannelMix.TryGetValue(x.Label, out var prev);
                    return new AnalyticsSeriesPoint(x.Label, x.Value, query.Compare ? prev : null);
                })
                .ToList(),
            Lists: channelMix,
            Table: table,
            Extras: new Dictionary<string, object?>
            {
                ["module"] = module,
                ["modules"] = new[] { "source_medium" },
                ["contractVersion"] = "v2",
                ["widgets"] = new[] { "channel_cards", "source_medium_table" }
            });
    }

    private static AnalyticsTabPayload BuildEngagementTabPayload(
        AnalyticsTabQuery query,
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyList<AnalyticsEvent> events,
        IReadOnlyList<AnalyticsPageView> previousViews,
        IReadOnlyDictionary<string, AnalyticsSession> sessionsById)
    {
        var rows = BuildPagesTableRows(views, events, sessionsById, 200);
        var previousRows = BuildPagesTableRows(previousViews, Array.Empty<AnalyticsEvent>(), sessionsById, 200);
        var previousUsers = previousViews.Select(v => v.SessionId).Distinct(StringComparer.Ordinal).Count();
        var hasEngagementSignals = views.Any(v => (v.EngagementTimeMs ?? 0) > 0);
        var hasTrackedEvents = events.Count > 0;
        var hasRevenueSignals = events.Any(e => ExtractRevenueValue(e.MetadataJson) > 0m);
        var filtered = ApplySearchSortPagination(rows, query, x => x.Page, x => x.Views);
        return new AnalyticsTabPayload(
            Tab: "engagement",
            Metrics: new[]
            {
                new AnalyticsKeyMetric("views","Views", views.Count, previousViews.Count),
                new AnalyticsKeyMetric("active_users","Active users", views.Select(v => v.SessionId).Distinct(StringComparer.Ordinal).Count(), query.Compare ? previousUsers : null),
                new AnalyticsKeyMetric("events","Event count", events.Count, null)
            },
            Series: rows
                .Take(8)
                .Select(row =>
                {
                    var prev = previousRows.FirstOrDefault(x => string.Equals(x.Page, row.Page, StringComparison.OrdinalIgnoreCase));
                    return new AnalyticsSeriesPoint(row.Page, row.Views, query.Compare ? prev?.Views : null);
                })
                .ToList(),
            Lists: events.GroupBy(e => e.EventName).Select(g => new AnalyticsListMetric(g.Key, g.Count())).OrderByDescending(x => x.Value).Take(10).ToList(),
            Table: new AnalyticsTablePayload(
                BuildPagesColumns(),
                filtered.Rows.Select(ToTableRow).ToList(),
                filtered.Total,
                filtered.Page,
                filtered.PageSize),
            Extras: new Dictionary<string, object?>
            {
                ["scrollDepth"] = new { low = 55, medium = 30, high = 15 },
                ["signalStatus"] = new
                {
                    hasEngagementSignals,
                    hasTrackedEvents,
                    hasRevenueSignals
                },
                ["contractVersion"] = "v2",
                ["widgets"] = new[] { "engagement_timeline", "scroll_depth", "top_events", "pages_screens_table" }
            });
    }

    private static AnalyticsTabPayload BuildAudienceTabPayload(
        AnalyticsTabQuery query,
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyList<AnalyticsEvent> events,
        IReadOnlyList<AnalyticsPageView> previousViews,
        IReadOnlyList<AnalyticsEvent> previousEvents,
        IReadOnlyDictionary<string, AnalyticsSession> sessionsById)
    {
        var dimension = NormalizeAudienceDimension(query.Dimension);
        var rows = BuildAudienceRowsByDimension(views, events, sessionsById, dimension);
        var previousRows = BuildAudienceRowsByDimension(previousViews, previousEvents, sessionsById, dimension)
            .ToDictionary(x => x.DimensionValue, StringComparer.OrdinalIgnoreCase);
        var hasEngagementSignals = views.Any(v => (v.EngagementTimeMs ?? 0) > 0);
        var hasTrackedEvents = events.Count > 0;
        var mappedRows = rows.Select(row =>
        {
            previousRows.TryGetValue(row.DimensionValue, out var previous);
            var engagementRate = row.Sessions == 0 ? 0 : (row.EngagedSessions * 100.0 / row.Sessions);
            var label = ToAudienceDimensionDisplay(dimension, row.DimensionValue);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dimension"] = label,
                ["active_users"] = row.ActiveUsers.ToString("N0"),
                ["new_users"] = row.NewUsers.ToString("N0"),
                ["engaged_sessions"] = row.EngagedSessions.ToString("N0"),
                ["engagement_rate"] = $"{engagementRate:0.##}%",
                ["avg_engagement"] = TimeSpan.FromMilliseconds(Math.Max(0, row.AvgEngagementMs)).ToString(@"m\:ss"),
                ["event_count"] = row.EventCount.ToString("N0"),
                ["key_events"] = row.KeyEvents.ToString("N0"),
                ["active_users_prev"] = previous?.ActiveUsers.ToString("N0") ?? "0"
            };
        }).ToList();
        var filtered = ApplyTextTableSearchSortPagination(mappedRows, query, "dimension", "active_users");
        var trend = rows
            .Take(10)
            .Select(x =>
            {
                previousRows.TryGetValue(x.DimensionValue, out var prev);
                return new AnalyticsSeriesPoint(ToAudienceDimensionDisplay(dimension, x.DimensionValue), x.ActiveUsers, query.Compare ? prev?.ActiveUsers : null);
            })
            .ToList();

        return new AnalyticsTabPayload(
            Tab: "audience",
            Metrics: new[]
            {
                new AnalyticsKeyMetric("countries","Rows", rows.Count),
                new AnalyticsKeyMetric("devices","Device categories", sessionsById.Values.Select(x => NormalizeDeviceType(x.DeviceType, null)).Distinct().Count()),
                new AnalyticsKeyMetric("browsers","Browsers", sessionsById.Values.Select(x => x.Browser ?? "unknown").Distinct().Count()),
                new AnalyticsKeyMetric("events","Event count", events.Count, query.Compare ? previousEvents.Count : null)
            },
            Series: trend,
            Lists: rows.Take(10).Select(x => new AnalyticsListMetric(ToAudienceDimensionDisplay(dimension, x.DimensionValue), x.ActiveUsers)).ToList(),
            Table: new AnalyticsTablePayload(
                new[]
                {
                    new AnalyticsTableColumn("dimension", ToAudienceColumnLabel(dimension), GetMetricHelpText(dimension == "country" ? "country" : "dimension")),
                    new AnalyticsTableColumn("active_users","Active users", GetMetricHelpText("active_users")),
                    new AnalyticsTableColumn("new_users","New users", GetMetricHelpText("new_users")),
                    new AnalyticsTableColumn("engaged_sessions","Engaged sessions", GetMetricHelpText("engaged_sessions")),
                    new AnalyticsTableColumn("engagement_rate","Engagement rate", GetMetricHelpText("engagement_rate")),
                    new AnalyticsTableColumn("avg_engagement","Avg engagement time", GetMetricHelpText("avg_engagement")),
                    new AnalyticsTableColumn("event_count","Event count", GetMetricHelpText("event_count")),
                    new AnalyticsTableColumn("key_events","Key events", GetMetricHelpText("key_events"))
                },
                filtered.Rows.Select(r => new AnalyticsTableRow(r)).ToList(),
                filtered.Total,
                filtered.Page,
                filtered.PageSize),
            Extras: new Dictionary<string, object?>
            {
                ["dimension"] = dimension,
                ["dimensionToggles"] = new[] { "country", "city", "device", "browser" },
                ["signalStatus"] = new
                {
                    hasEngagementSignals,
                    hasTrackedEvents
                },
                ["contractVersion"] = "v2",
                ["widgets"] = new[] { "dimension_toggle", "audience_trend", "audience_table" }
            });
    }

    private async Task<AnalyticsTabPayload> BuildConversionsTabPayloadAsync(
        string ownerUserId,
        Guid? companyId,
        int projectId,
        AnalyticsTabQuery query,
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyList<AnalyticsPageView> previousViews,
        IReadOnlyList<AnalyticsEvent> events,
        IReadOnlyList<AnalyticsEvent> previousEvents,
        IReadOnlyDictionary<string, AnalyticsSession> sessionsById,
        CancellationToken cancellationToken)
    {
        var conversionEvents = events.Where(e => IsPrimaryConversionEvent(e.EventType, e.EventName)).ToList();
        var previousConversions = previousEvents.Where(e => IsPrimaryConversionEvent(e.EventType, e.EventName)).ToList();
        var keyEvents = events
            .Where(e => IsKeyEvent(e.EventType, e.EventName))
            .GroupBy(e => $"{e.EventType}:{e.EventName}")
            .Select(g => new AnalyticsListMetric(g.Key, g.Count()))
            .OrderByDescending(x => x.Value)
            .ToList();
        var previousKeyEvents = previousEvents.Count(e => IsKeyEvent(e.EventType, e.EventName));
        var channelAttribution = conversionEvents
            .Select(e => sessionsById.TryGetValue(e.SessionId, out var session) ? NormalizeChannel(session.Channel) : "direct")
            .GroupBy(x => x)
            .Select(g => new AnalyticsListMetric(ToChannelLabel(g.Key), g.Count()))
            .OrderByDescending(x => x.Value)
            .Take(6)
            .ToList();
        var funnelMode = NormalizeFunnelMode(query.FunnelMode);
        var customFunnels = await GetCustomFunnelsAsync(ownerUserId, companyId, projectId, cancellationToken);
        var selectedFunnel = customFunnels.FirstOrDefault(x => x.Id == query.FunnelId) ?? customFunnels.FirstOrDefault();
        var customFunnelPayload = BuildCustomFunnel(
            views,
            previousViews,
            events,
            previousEvents,
            selectedFunnel?.Steps ?? Array.Empty<CustomFunnelStep>(),
            funnelMode);
        return new AnalyticsTabPayload(
            Tab: "conversions",
            Metrics: new[]
            {
                new AnalyticsKeyMetric("key_events","Key events", keyEvents.Sum(x => x.Value), query.Compare ? previousKeyEvents : null),
                new AnalyticsKeyMetric("funnel_rate","Funnel conversion", customFunnelPayload.CurrentOverallRate, query.Compare ? customFunnelPayload.PreviousOverallRate : null, "%"),
                new AnalyticsKeyMetric("unique_converters","Unique converters", conversionEvents.Select(x => x.SessionId).Distinct().Count())
            },
            Series: customFunnelPayload.Steps.Select(s =>
            {
                var prev = customFunnelPayload.PreviousStepCounts.TryGetValue(s.Key, out var count) ? count : (double?)null;
                return new AnalyticsSeriesPoint(s.Label, s.Sessions, query.Compare ? prev : null);
            }).ToList(),
            Lists: keyEvents.Take(10).ToList(),
            Table: new AnalyticsTablePayload(
                new[]
                {
                    new AnalyticsTableColumn("step", "Step", GetMetricHelpText("step")),
                    new AnalyticsTableColumn("sessions", "Sessions", GetMetricHelpText("sessions")),
                    new AnalyticsTableColumn("dropoff", "Drop-off %", GetMetricHelpText("dropoff"))
                },
                customFunnelPayload.Steps.Select(s => new AnalyticsTableRow(new Dictionary<string, string>
                {
                    ["step"] = s.Label,
                    ["sessions"] = s.Sessions.ToString("N0"),
                    ["dropoff"] = $"{s.DropoffPercent:0.##}%"
                })).ToList(),
                customFunnelPayload.Steps.Count,
                1,
                customFunnelPayload.Steps.Count),
            Extras: new Dictionary<string, object?>
            {
                ["attribution"] = channelAttribution,
                ["funnelMode"] = funnelMode,
                ["funnelModes"] = new[] { "open", "closed" },
                ["signalStatus"] = new
                {
                    hasConversionEvents = conversionEvents.Count > 0,
                    hasKeyEvents = keyEvents.Count > 0,
                    hasAttribution = channelAttribution.Count > 0
                },
                ["funnelBuilder"] = new
                {
                    selectedFunnelId = selectedFunnel?.Id,
                    funnels = customFunnels.Select(x => new { x.Id, x.Name, x.IsSystemPreset }),
                    steps = customFunnelPayload.Steps.Select(x => new { x.Key, x.Label }),
                    availableSteps = BuildFunnelStepCandidates(views, events),
                    editable = selectedFunnel is null || !selectedFunnel.IsSystemPreset
                },
                ["contractVersion"] = "v2",
                ["widgets"] = new[] { "funnel_builder", "key_events", "attribution" }
            });
    }

    private static AnalyticsTabPayload BuildSeoTabPayload()
    {
        return new AnalyticsTabPayload(
            Tab: "seo",
            Metrics: new[]
            {
                new AnalyticsKeyMetric("indexed","Indexed",0),
                new AnalyticsKeyMetric("not_indexed","Not indexed",0),
                new AnalyticsKeyMetric("issues","Issues",0)
            },
            Series: Array.Empty<AnalyticsSeriesPoint>(),
            Lists: Array.Empty<AnalyticsListMetric>(),
            Table: null,
            Extras: new Dictionary<string, object?>
            {
                ["mode"] = "connector_ready",
                ["panels"] = new[] { "indexing_status", "issues", "sitemaps", "search_performance" },
                ["contractVersion"] = "v2",
                ["widgets"] = new[] { "connector_status", "indexing_cards", "issues_list", "sitemaps", "search_performance" }
            });
    }

    private static AnalyticsTabPayload BuildPagesTabPayload(
        AnalyticsTabQuery query,
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyList<AnalyticsEvent> events,
        IReadOnlyDictionary<string, AnalyticsSession> sessionsById)
    {
        var rows = BuildPagesTableRows(views, events, sessionsById, 500);
        var filtered = ApplySearchSortPagination(rows, query, x => x.Page, x => x.Views);
        return new AnalyticsTabPayload(
            Tab: "pages",
            Metrics: new[]
            {
                new AnalyticsKeyMetric("pages","Pages", rows.Count),
                new AnalyticsKeyMetric("views","Views", views.Count),
                new AnalyticsKeyMetric("key_events","Key events", events.Count(e => IsPrimaryConversionEvent(e.EventType, e.EventName)))
            },
            Series: Array.Empty<AnalyticsSeriesPoint>(),
            Lists: Array.Empty<AnalyticsListMetric>(),
            Table: new AnalyticsTablePayload(
                BuildPagesColumns(),
                filtered.Rows.Select(ToTableRow).ToList(),
                filtered.Total,
                filtered.Page,
                filtered.PageSize),
            Extras: new Dictionary<string, object?>
            {
                ["signalStatus"] = new
                {
                    hasEngagementSignals = views.Any(v => (v.EngagementTimeMs ?? 0) > 0),
                    hasTrackedEvents = events.Count > 0,
                    hasRevenueSignals = events.Any(e => ExtractRevenueValue(e.MetadataJson) > 0m)
                },
                ["contractVersion"] = "v2",
                ["widgets"] = new[] { "pages_screens_table" }
            });
    }

    private sealed record AudienceDimensionRow(
        string DimensionValue,
        int ActiveUsers,
        int NewUsers,
        int Sessions,
        int EngagedSessions,
        int AvgEngagementMs,
        int EventCount,
        int KeyEvents);

    private static IReadOnlyList<AudienceDimensionRow> BuildAudienceRowsByDimension(
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyList<AnalyticsEvent> events,
        IReadOnlyDictionary<string, AnalyticsSession> sessionsById,
        string dimension)
    {
        var viewsBySession = views.GroupBy(v => v.SessionId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var eventsBySession = events.GroupBy(e => e.SessionId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var rows = viewsBySession
            .Select(kvp =>
            {
                var sessionId = kvp.Key;
                var sessionViews = kvp.Value;
                sessionsById.TryGetValue(sessionId, out var session);
                var key = dimension switch
                {
                    "city" => string.IsNullOrWhiteSpace(session?.City) ? "Unknown" : session.City!,
                    "language" => string.IsNullOrWhiteSpace(session?.Language) ? "unknown" : session.Language!,
                    "device" => NormalizeDeviceType(session?.DeviceType, null),
                    "browser" => ToBrowserLabel(session?.Browser),
                    "browser_os" => $"{(string.IsNullOrWhiteSpace(session?.Browser) ? "other" : session.Browser)}/{(string.IsNullOrWhiteSpace(session?.Os) ? "other" : session.Os)}",
                    _ => NormalizeCountry(session?.CountryCode)
                };
                eventsBySession.TryGetValue(sessionId, out var sessionEvents);
                sessionEvents ??= new List<AnalyticsEvent>();
                return new
                {
                    Dimension = key,
                    SessionId = sessionId,
                    Views = sessionViews,
                    Events = sessionEvents
                };
            })
            .GroupBy(x => x.Dimension)
            .Select(g =>
            {
                var sessions = g.ToList();
                var viewCount = sessions.SelectMany(x => x.Views).Count();
                var engagedSessions = sessions.Count(x => x.Views.Any(v => (v.EngagementTimeMs ?? 0) > 0));
                var avgEngagement = sessions
                    .SelectMany(x => x.Views)
                    .Select(x => x.EngagementTimeMs ?? 0)
                    .DefaultIfEmpty(0)
                    .Average();
                var eventCount = sessions.SelectMany(x => x.Events).Count();
                var keyEvents = sessions.SelectMany(x => x.Events).Count(e => IsKeyEvent(e.EventType, e.EventName));
                return new AudienceDimensionRow(
                    DimensionValue: g.Key,
                    ActiveUsers: sessions.Select(x => x.SessionId).Distinct(StringComparer.Ordinal).Count(),
                    NewUsers: sessions.Count,
                    Sessions: viewCount,
                    EngagedSessions: engagedSessions,
                    AvgEngagementMs: (int)Math.Round(avgEngagement),
                    EventCount: eventCount,
                    KeyEvents: keyEvents);
            })
            .OrderByDescending(x => x.ActiveUsers)
            .Take(200)
            .ToList();
        return rows;
    }

    private static string ToAudienceColumnLabel(string dimension) => dimension switch
    {
        "city" => "City",
        "device" => "Device",
        "browser" => "Browser",
        "browser_os" => "Browser / OS",
        _ => "Country"
    };

    private static string ToAudienceDimensionDisplay(string dimension, string? value)
    {
        var raw = string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
        return dimension switch
        {
            "country" => ToCountryDisplayLabel(raw),
            "device" => NormalizeDeviceType(raw, null),
            "browser" => ToBrowserLabel(raw),
            _ => raw
        };
    }

    private static string NormalizeAudienceDimension(string? dimension)
    {
        var normalized = (dimension ?? "country").Trim().ToLowerInvariant();
        return normalized is "country" or "city" or "device" or "browser" ? normalized : "country";
    }

    private static string ToCountryDisplayLabel(string? code)
    {
        var normalized = NormalizeCountry(code);
        if (string.IsNullOrWhiteSpace(normalized) || normalized is "UNK" or "ZZ")
        {
            return "Unknown";
        }

        try
        {
            return $"{new RegionInfo(normalized).EnglishName} ({normalized})";
        }
        catch
        {
            return normalized;
        }
    }

    private static decimal ExtractRevenueValue(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return 0m;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(metadataJson);
            return TryReadRevenueValue(doc.RootElement);
        }
        catch
        {
            return 0m;
        }
    }

    private static decimal TryReadRevenueValue(System.Text.Json.JsonElement element)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var name in new[] { "value", "revenue", "amount", "total" })
            {
                if (element.TryGetProperty(name, out var child))
                {
                    var parsed = TryReadRevenueValue(child);
                    if (parsed > 0m)
                    {
                        return parsed;
                    }
                }
            }
        }

        if (element.ValueKind == System.Text.Json.JsonValueKind.Number && element.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        if (element.ValueKind == System.Text.Json.JsonValueKind.String &&
            decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var stringValue))
        {
            return stringValue;
        }

        return 0m;
    }

    private static string? GetMetricHelpText(string key) => key switch
    {
        "page" => "The page path or title that received traffic in the selected range.",
        "views" => "Total page views recorded for this row in the selected time window.",
        "active_users" => "Distinct sessions or users who visited this row during the selected range.",
        "views_per_user" => "Average number of views generated by each active user for this page. Higher means repeat viewing.",
        "avg_engagement" => "Average engaged time recorded for this row. If Bugence did not receive engagement beacons, this will stay untracked.",
        "event_count" => "All tracked custom analytics events tied to this row in the selected range.",
        "key_events" => "High-value events such as form submits, lead actions, WhatsApp clicks, pricing intent, or other mapped goal events.",
        "revenue" => "Tracked monetary value attributed to this row. This stays untracked until value or purchase metadata is sent with events.",
        "country" => "Geographic dimension based on the visitor country detected for the session.",
        "dimension" => "The selected audience dimension used to group this report.",
        "new_users" => "Sessions appearing as first-time visitors in the current grouping.",
        "engaged_sessions" => "Sessions with a positive engagement signal, typically measured through engagement time or follow-on interaction.",
        "engagement_rate" => "Percentage of sessions in this group that were engaged instead of immediately idle.",
        "step" => "A journey step in the configured conversion funnel.",
        "sessions" => "How many sessions reached this step or record in the selected range.",
        "dropoff" => "Percentage of sessions that did not progress beyond this step.",
        _ => null
    };

    private static string ToBrowserLabel(string? browser)
    {
        return (browser ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "chrome" => "Chrome",
            "safari" => "Safari",
            "firefox" => "Firefox",
            "edge" => "Edge",
            "ie" => "IE",
            "other" or "" => "Other",
            var value => char.ToUpperInvariant(value[0]) + value[1..]
        };
    }

    private static string NormalizeAcquisitionModule(string? module)
    {
        var normalized = (module ?? "source_medium").Trim().ToLowerInvariant();
        return "source_medium";
    }

    private static string NormalizeFunnelMode(string? mode)
    {
        var normalized = (mode ?? "open").Trim().ToLowerInvariant();
        return normalized is "open" or "closed" ? normalized : "open";
    }

    private static bool IsKeyEvent(string? eventType, string? eventName)
    {
        if (IsPrimaryConversionEvent(eventType, eventName))
        {
            return true;
        }

        var candidate = $"{eventType}:{eventName}".ToLowerInvariant();
        return candidate.Contains("whatsapp", StringComparison.Ordinal) ||
               candidate.Contains("call", StringComparison.Ordinal) ||
               candidate.Contains("pricing", StringComparison.Ordinal) ||
               candidate.Contains("lead", StringComparison.Ordinal);
    }

    private static IReadOnlyList<Dictionary<string, string>> BuildSourceMediumRows(
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyList<AnalyticsEvent> events)
    {
        var eventsBySession = events.GroupBy(e => e.SessionId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var source = g.Select(x => x.UtmSource).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "direct";
                    var medium = g.Select(x => x.UtmMedium).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "none";
                    return new { source, medium, keyEvents = g.Count(x => IsPrimaryConversionEvent(x.EventType, x.EventName)) };
                },
                StringComparer.Ordinal);
        return views
            .GroupBy(v => v.SessionId, StringComparer.Ordinal)
            .Select(g =>
            {
                var sessionId = g.Key;
                var first = eventsBySession.TryGetValue(sessionId, out var details)
                    ? details
                    : new { source = "direct", medium = "none", keyEvents = 0 };
                return new
                {
                    SourceMedium = $"{first.source}/{first.medium}",
                    SessionId = sessionId,
                    KeyEvents = first.keyEvents
                };
            })
            .GroupBy(x => x.SourceMedium)
            .Select(g =>
            {
                var sessionCount = g.Select(x => x.SessionId).Distinct(StringComparer.Ordinal).Count();
                var users = sessionCount;
                var keyEvents = g.Sum(x => x.KeyEvents);
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["source_medium"] = g.Key,
                    ["sessions"] = sessionCount.ToString(),
                    ["users"] = users.ToString(),
                    ["key_events"] = keyEvents.ToString()
                };
            })
            .OrderByDescending(x => ParseInt(x, "sessions"))
            .ToList();
    }

    private static IReadOnlyList<Dictionary<string, string>> BuildCampaignRows(
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyList<AnalyticsEvent> events)
    {
        var eventsBySession = events.GroupBy(e => e.SessionId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        return views
            .GroupBy(v => v.SessionId, StringComparer.Ordinal)
            .Select(g =>
            {
                eventsBySession.TryGetValue(g.Key, out var sessionEvents);
                sessionEvents ??= new List<AnalyticsEvent>();
                var campaign = sessionEvents.Select(x => x.UtmCampaign).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "(not set)";
                var source = sessionEvents.Select(x => x.UtmSource).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "direct";
                var medium = sessionEvents.Select(x => x.UtmMedium).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "none";
                return new
                {
                    campaign,
                    sourceMedium = $"{source}/{medium}",
                    keyEvents = sessionEvents.Count(x => IsPrimaryConversionEvent(x.EventType, x.EventName)),
                    sessionId = g.Key
                };
            })
            .GroupBy(x => new { x.campaign, x.sourceMedium })
            .Select(g => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["campaign"] = g.Key.campaign,
                ["source_medium"] = g.Key.sourceMedium,
                ["sessions"] = g.Select(x => x.sessionId).Distinct(StringComparer.Ordinal).Count().ToString(),
                ["key_events"] = g.Sum(x => x.keyEvents).ToString()
            })
            .OrderByDescending(x => ParseInt(x, "sessions"))
            .ToList();
    }

    private static IReadOnlyList<Dictionary<string, string>> BuildLandingRows(
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyList<AnalyticsEvent> events)
    {
        var conversionSessions = events
            .Where(e => IsPrimaryConversionEvent(e.EventType, e.EventName))
            .Select(e => e.SessionId)
            .ToHashSet(StringComparer.Ordinal);
        return views
            .GroupBy(v => NormalizePath(v.LandingPath ?? v.Path))
            .Select(g =>
            {
                var sessions = g.Select(x => x.SessionId).Distinct(StringComparer.Ordinal).ToList();
                var keyEvents = sessions.Count(id => conversionSessions.Contains(id));
                var conversionRate = sessions.Count == 0 ? 0 : keyEvents * 100.0 / sessions.Count;
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["landing_page"] = g.Key,
                    ["sessions"] = sessions.Count.ToString(),
                    ["conversion_rate"] = conversionRate.ToString("0.##"),
                    ["key_events"] = keyEvents.ToString()
                };
            })
            .OrderByDescending(x => ParseInt(x, "sessions"))
            .ToList();
    }

    private sealed record TextPageResult(IReadOnlyList<Dictionary<string, string>> Rows, int Total, int Page, int PageSize);

    private static TextPageResult ApplyTextTableSearchSortPagination(
        IReadOnlyList<Dictionary<string, string>> rows,
        AnalyticsTabQuery query,
        string defaultSearchKey,
        string defaultSortKey)
    {
        IEnumerable<Dictionary<string, string>> working = rows;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            working = working.Where(row => row.Any(kvp => kvp.Value.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        var sortBy = string.IsNullOrWhiteSpace(query.SortBy) ? defaultSortKey : query.SortBy.Trim().ToLowerInvariant();
        var desc = !string.Equals(query.SortDir, "asc", StringComparison.OrdinalIgnoreCase);
        var sortAsNumeric = rows.Any(r => r.TryGetValue(sortBy, out var v) && double.TryParse(v.Replace(",", string.Empty, StringComparison.Ordinal).Replace("%", string.Empty, StringComparison.Ordinal), out _));
        working = sortAsNumeric
            ? (desc
                ? working.OrderByDescending(row => ParseForNumericSort(row, sortBy)).ThenBy(row => row.TryGetValue(defaultSearchKey, out var key) ? key : string.Empty)
                : working.OrderBy(row => ParseForNumericSort(row, sortBy)).ThenBy(row => row.TryGetValue(defaultSearchKey, out var key) ? key : string.Empty))
            : (desc
                ? working.OrderByDescending(row => row.TryGetValue(sortBy, out var key) ? key : string.Empty).ThenBy(row => row.TryGetValue(defaultSearchKey, out var key) ? key : string.Empty)
                : working.OrderBy(row => row.TryGetValue(sortBy, out var key) ? key : string.Empty).ThenBy(row => row.TryGetValue(defaultSearchKey, out var key) ? key : string.Empty));

        var total = working.Count();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var paged = working.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new TextPageResult(paged, total, page, pageSize);
    }

    private static double ParseForNumericSort(Dictionary<string, string> row, string key)
    {
        if (!row.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return 0d;
        }
        var normalized = raw.Replace("%", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal);
        if (double.TryParse(normalized, out var numeric))
        {
            return numeric;
        }
        return 0d;
    }

    private static int ParseInt(Dictionary<string, string> row, string key)
    {
        if (!row.TryGetValue(key, out var raw))
        {
            return 0;
        }
        var normalized = raw.Replace(",", string.Empty, StringComparison.Ordinal);
        return int.TryParse(normalized, out var parsed) ? parsed : 0;
    }

    private static (double lat, double lon)? TryMapCountryToLatLon(string countryCode)
    {
        return NormalizeCountry(countryCode) switch
        {
            "US" => (39.8283, -98.5795),
            "CA" => (56.1304, -106.3468),
            "MX" => (23.6345, -102.5528),
            "GB" => (55.3781, -3.4360),
            "DE" => (51.1657, 10.4515),
            "FR" => (46.2276, 2.2137),
            "IT" => (41.8719, 12.5674),
            "ES" => (40.4637, -3.7492),
            "IN" => (20.5937, 78.9629),
            "PK" => (30.3753, 69.3451),
            "BD" => (23.6850, 90.3563),
            "AU" => (-25.2744, 133.7751),
            "JP" => (36.2048, 138.2529),
            "CN" => (35.8617, 104.1954),
            "BR" => (-14.2350, -51.9253),
            "ZA" => (-30.5595, 22.9375),
            _ => null
        };
    }

    private static IReadOnlyList<object> BuildFunnelStepCandidates(
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyList<AnalyticsEvent> events)
    {
        var pageCandidates = views
            .GroupBy(v => NormalizePath(v.Path))
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => (object)new { key = $"page:{g.Key}", label = $"Page view {g.Key}", type = "page" });
        var eventCandidates = events
            .GroupBy(e => $"{e.EventType}:{e.EventName}")
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => (object)new { key = $"event:{g.Key}", label = $"Event {g.Key}", type = "event" });
        return pageCandidates.Concat(eventCandidates).ToList();
    }

    private sealed record CustomFunnelDef(Guid Id, string Name, bool IsSystemPreset, IReadOnlyList<CustomFunnelStep> Steps);
    private sealed record CustomFunnelStep(string Type, string Key, string Label);
    private sealed record ComputedCustomFunnel(
        IReadOnlyList<AnalyticsFunnelStep> Steps,
        IReadOnlyDictionary<string, double> PreviousStepCounts,
        double CurrentOverallRate,
        double PreviousOverallRate);

    private async Task<IReadOnlyList<CustomFunnelDef>> GetCustomFunnelsAsync(
        string ownerUserId,
        Guid? companyId,
        int projectId,
        CancellationToken cancellationToken)
    {
        var query = _db.AnalyticsCustomFunnels.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.OwnerUserId == ownerUserId);
        if (companyId.HasValue)
        {
            query = query.Where(x => x.CompanyId == companyId.Value);
        }
        var rows = await query.OrderByDescending(x => x.IsSystemPreset).ThenBy(x => x.Name).ToListAsync(cancellationToken);
        return rows.Select(row =>
        {
            var steps = ParseCustomSteps(row.StepsJson);
            return new CustomFunnelDef(row.Id, row.Name, row.IsSystemPreset, steps);
        }).ToList();
    }

    private static IReadOnlyList<CustomFunnelStep> ParseCustomSteps(string? stepsJson)
    {
        if (string.IsNullOrWhiteSpace(stepsJson))
        {
            return Array.Empty<CustomFunnelStep>();
        }

        try
        {
            var doc = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(stepsJson);
            if (doc.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return Array.Empty<CustomFunnelStep>();
            }
            return doc.EnumerateArray()
                .Select(x =>
                {
                    var type = x.TryGetProperty("type", out var t) ? (t.GetString() ?? "page") : "page";
                    var key = x.TryGetProperty("key", out var k) ? (k.GetString() ?? string.Empty) : string.Empty;
                    var label = x.TryGetProperty("label", out var l) ? (l.GetString() ?? key) : key;
                    return new CustomFunnelStep(type, key, string.IsNullOrWhiteSpace(label) ? key : label);
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .Take(12)
                .ToList();
        }
        catch
        {
            return Array.Empty<CustomFunnelStep>();
        }
    }

    private static ComputedCustomFunnel BuildCustomFunnel(
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyList<AnalyticsPageView> previousViews,
        IReadOnlyList<AnalyticsEvent> events,
        IReadOnlyList<AnalyticsEvent> previousEvents,
        IReadOnlyList<CustomFunnelStep> steps,
        string funnelMode)
    {
        if (steps.Count == 0)
        {
            var fallback = BuildFunnel(views, events.Where(e => IsPrimaryConversionEvent(e.EventType, e.EventName)).ToList());
            var previousFallback = BuildFunnel(previousViews, previousEvents.Where(e => IsPrimaryConversionEvent(e.EventType, e.EventName)).ToList());
            return new ComputedCustomFunnel(
                fallback.Steps,
                previousFallback.Steps.ToDictionary(x => x.Key, x => (double)x.Sessions, StringComparer.OrdinalIgnoreCase),
                fallback.OverallConversionRatePercent,
                previousFallback.OverallConversionRatePercent);
        }

        var currentSessionUniverse = views.Select(v => v.SessionId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var previousSessionUniverse = previousViews.Select(v => v.SessionId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var currentStepCounts = EvaluateCustomStepCounts(views, events, currentSessionUniverse, steps, funnelMode);
        var previousStepCounts = EvaluateCustomStepCounts(previousViews, previousEvents, previousSessionUniverse, steps, funnelMode);
        var computedSteps = new List<AnalyticsFunnelStep>(steps.Count);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var current = currentStepCounts.TryGetValue(step.Key, out var c) ? c : 0;
            var next = i < steps.Count - 1
                ? (currentStepCounts.TryGetValue(steps[i + 1].Key, out var n) ? n : 0)
                : current;
            var dropCount = Math.Max(0, current - next);
            var dropPct = current == 0 ? 0 : dropCount * 100.0 / current;
            var stepToNext = i < steps.Count - 1 ? (current == 0 ? 0 : next * 100.0 / current) : 100;
            computedSteps.Add(new AnalyticsFunnelStep(step.Key, step.Label, current, dropCount, dropPct, stepToNext));
        }
        var firstCurrent = computedSteps.FirstOrDefault()?.Sessions ?? 0;
        var lastCurrent = computedSteps.LastOrDefault()?.Sessions ?? 0;
        var firstPrev = previousStepCounts.TryGetValue(steps.First().Key, out var f) ? f : 0;
        var lastPrev = previousStepCounts.TryGetValue(steps.Last().Key, out var l) ? l : 0;
        return new ComputedCustomFunnel(
            computedSteps,
            previousStepCounts.ToDictionary(x => x.Key, x => (double)x.Value, StringComparer.OrdinalIgnoreCase),
            firstCurrent == 0 ? 0 : (lastCurrent * 100.0 / firstCurrent),
            firstPrev == 0 ? 0 : (lastPrev * 100.0 / firstPrev));
    }

    private static Dictionary<string, int> EvaluateCustomStepCounts(
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyList<AnalyticsEvent> events,
        HashSet<string> universe,
        IReadOnlyList<CustomFunnelStep> steps,
        string funnelMode)
    {
        var sessionsByPage = views.GroupBy(v => NormalizePath(v.Path))
            .ToDictionary(g => g.Key, g => g.Select(x => x.SessionId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);
        var sessionsByEvent = events.GroupBy(e => $"{e.EventType}:{e.EventName}")
            .ToDictionary(g => g.Key, g => g.Select(x => x.SessionId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);

        var remaining = new HashSet<string>(universe, StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            HashSet<string> matched = step.Type switch
            {
                "event" => sessionsByEvent.TryGetValue(step.Key, out var eventSessions)
                    ? new HashSet<string>(eventSessions, StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal),
                _ => sessionsByPage.TryGetValue(NormalizePath(step.Key), out var pageSessions)
                    ? new HashSet<string>(pageSessions, StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal)
            };
            if (string.Equals(funnelMode, "closed", StringComparison.OrdinalIgnoreCase))
            {
                matched.IntersectWith(remaining);
                remaining = matched;
            }
            counts[step.Key] = matched.Count;
        }
        return counts;
    }

    private static void ApplySegmentAndFilters(
        AnalyticsTabQuery query,
        IReadOnlyDictionary<string, AnalyticsSession> sessionsById,
        ref List<AnalyticsPageView> views,
        ref List<AnalyticsEvent> events)
    {
        if (!string.Equals(query.Segment, "all", StringComparison.OrdinalIgnoreCase))
        {
            var segmented = views.Where(v =>
            {
                if (!sessionsById.TryGetValue(v.SessionId, out var s))
                {
                    return false;
                }
                return string.Equals(NormalizeChannel(s.Channel), NormalizeSegment(query.Segment), StringComparison.OrdinalIgnoreCase);
            }).ToList();
            var allowed = segmented.Select(v => v.SessionId).ToHashSet(StringComparer.Ordinal);
            views = segmented;
            events = events.Where(e => allowed.Contains(e.SessionId)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(query.Filters.Country))
        {
            var filter = NormalizeCountry(query.Filters.Country);
            views = views.Where(v => sessionsById.TryGetValue(v.SessionId, out var s) && NormalizeCountry(s.CountryCode) == filter).ToList();
            var allowed = views.Select(v => v.SessionId).ToHashSet(StringComparer.Ordinal);
            events = events.Where(e => allowed.Contains(e.SessionId)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(query.Filters.Device))
        {
            var device = NormalizeDeviceType(query.Filters.Device, null);
            views = views.Where(v => sessionsById.TryGetValue(v.SessionId, out var s) && NormalizeDeviceType(s.DeviceType, null) == device).ToList();
            var allowed = views.Select(v => v.SessionId).ToHashSet(StringComparer.Ordinal);
            events = events.Where(e => allowed.Contains(e.SessionId)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(query.Filters.Referrer))
        {
            var referrer = query.Filters.Referrer.Trim().ToLowerInvariant();
            views = views.Where(v => sessionsById.TryGetValue(v.SessionId, out var s) && (s.ReferrerHost ?? "direct").Contains(referrer, StringComparison.OrdinalIgnoreCase)).ToList();
            var allowed = views.Select(v => v.SessionId).ToHashSet(StringComparer.Ordinal);
            events = events.Where(e => allowed.Contains(e.SessionId)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(query.Filters.LandingPage))
        {
            var page = NormalizePath(query.Filters.LandingPage);
            views = views.Where(v => NormalizePath(v.LandingPath ?? v.Path) == page).ToList();
            var allowed = views.Select(v => v.SessionId).ToHashSet(StringComparer.Ordinal);
            events = events.Where(e => allowed.Contains(e.SessionId)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(query.Filters.UtmSource))
        {
            events = events.Where(e => string.Equals(e.UtmSource, query.Filters.UtmSource, StringComparison.OrdinalIgnoreCase)).ToList();
            var allowed = events.Select(e => e.SessionId).ToHashSet(StringComparer.Ordinal);
            views = views.Where(v => allowed.Contains(v.SessionId)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(query.Filters.UtmMedium))
        {
            events = events.Where(e => string.Equals(e.UtmMedium, query.Filters.UtmMedium, StringComparison.OrdinalIgnoreCase)).ToList();
            var allowed = events.Select(e => e.SessionId).ToHashSet(StringComparer.Ordinal);
            views = views.Where(v => allowed.Contains(v.SessionId)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(query.Filters.UtmCampaign))
        {
            events = events.Where(e => string.Equals(e.UtmCampaign, query.Filters.UtmCampaign, StringComparison.OrdinalIgnoreCase)).ToList();
            var allowed = events.Select(e => e.SessionId).ToHashSet(StringComparer.Ordinal);
            views = views.Where(v => allowed.Contains(v.SessionId)).ToList();
        }
    }

    private static IReadOnlyList<AnalyticsSeriesPoint> BuildSimpleSeries(string range, IReadOnlyList<double> data24h, IReadOnlyList<double> data7d, IReadOnlyList<double> data30d)
    {
        var src = range switch
        {
            "24h" => data24h,
            "7d" => data7d,
            _ => data30d
        };
        return src.Select((v, i) => new AnalyticsSeriesPoint((i + 1).ToString(), v)).ToList();
    }

    private sealed record PageMetricRow(string Page, int Views, int ActiveUsers, double ViewsPerActiveUser, int AvgEngagementTimeMs, int EventCount, int KeyEvents, decimal Revenue);

    private static IReadOnlyList<PageMetricRow> BuildPagesTableRows(
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyList<AnalyticsEvent> events,
        IReadOnlyDictionary<string, AnalyticsSession> sessionsById,
        int take)
    {
        var grouped = views
            .GroupBy(v => NormalizePath(v.Path))
            .Select(g =>
            {
                var sessionIds = g.Select(x => x.SessionId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
                var pageEvents = events.Where(e => sessionIds.Contains(e.SessionId) && NormalizePath(e.Path) == g.Key).ToList();
                var activeUsers = sessionIds.Count;
                var viewCount = g.Count();
                var avgEngagement = g.Select(x => x.EngagementTimeMs ?? 0).DefaultIfEmpty(0).Average();
                var keyEvents = pageEvents.Count(e => IsKeyEvent(e.EventType, e.EventName));
                var revenue = pageEvents.Sum(e => ExtractRevenueValue(e.MetadataJson));
                return new PageMetricRow(
                    Page: g.Key,
                    Views: viewCount,
                    ActiveUsers: activeUsers,
                    ViewsPerActiveUser: activeUsers == 0 ? 0 : viewCount / (double)activeUsers,
                    AvgEngagementTimeMs: (int)Math.Round(avgEngagement),
                    EventCount: pageEvents.Count,
                    KeyEvents: keyEvents,
                    Revenue: revenue);
            })
            .OrderByDescending(x => x.Views)
            .Take(take)
            .ToList();
        return grouped;
    }

    private static IReadOnlyList<AnalyticsTableColumn> BuildPagesColumns() => new[]
    {
        new AnalyticsTableColumn("page", "Page title / URL", GetMetricHelpText("page")),
        new AnalyticsTableColumn("views", "Views", GetMetricHelpText("views")),
        new AnalyticsTableColumn("active_users", "Active users", GetMetricHelpText("active_users")),
        new AnalyticsTableColumn("views_per_user", "Views / active user", GetMetricHelpText("views_per_user")),
        new AnalyticsTableColumn("avg_engagement", "Avg engagement time", GetMetricHelpText("avg_engagement")),
        new AnalyticsTableColumn("event_count", "Event count", GetMetricHelpText("event_count")),
        new AnalyticsTableColumn("key_events", "Key events", GetMetricHelpText("key_events")),
        new AnalyticsTableColumn("revenue", "Revenue", GetMetricHelpText("revenue"))
    };

    private static AnalyticsTableRow ToTableRow(PageMetricRow row) => new(new Dictionary<string, string>
    {
        ["page"] = row.Page,
        ["views"] = row.Views.ToString("N0"),
        ["active_users"] = row.ActiveUsers.ToString("N0"),
        ["views_per_user"] = row.ViewsPerActiveUser.ToString("0.##"),
        ["avg_engagement"] = TimeSpan.FromMilliseconds(Math.Max(0, row.AvgEngagementTimeMs)).ToString(@"m\:ss"),
        ["event_count"] = row.EventCount.ToString("N0"),
        ["key_events"] = row.KeyEvents.ToString("N0"),
        ["revenue"] = row.Revenue.ToString("0.##")
    });

    private sealed record PageResult<T>(IReadOnlyList<T> Rows, int Total, int Page, int PageSize);

    private static PageResult<PageMetricRow> ApplySearchSortPagination(
        IReadOnlyList<PageMetricRow> rows,
        AnalyticsTabQuery query,
        Func<PageMetricRow, string> searchField,
        Func<PageMetricRow, int> defaultSort)
    {
        IEnumerable<PageMetricRow> working = rows;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            working = working.Where(r => searchField(r).Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        var sortBy = (query.SortBy ?? "views").Trim().ToLowerInvariant();
        var desc = !string.Equals(query.SortDir, "asc", StringComparison.OrdinalIgnoreCase);
        working = sortBy switch
        {
            "active_users" => desc ? working.OrderByDescending(x => x.ActiveUsers) : working.OrderBy(x => x.ActiveUsers),
            "event_count" => desc ? working.OrderByDescending(x => x.EventCount) : working.OrderBy(x => x.EventCount),
            "key_events" => desc ? working.OrderByDescending(x => x.KeyEvents) : working.OrderBy(x => x.KeyEvents),
            _ => desc ? working.OrderByDescending(defaultSort) : working.OrderBy(defaultSort)
        };

        var total = working.Count();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var paged = working.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PageResult<PageMetricRow>(paged, total, page, pageSize);
    }

    private static IReadOnlyList<AnalyticsListMetric> BuildChannelMix(
        IReadOnlyList<AnalyticsPageView> views,
        IReadOnlyDictionary<string, AnalyticsSession> sessionsById)
    {
        var grouped = views
            .Select(v => sessionsById.TryGetValue(v.SessionId, out var s) ? NormalizeChannel(s.Channel) : "direct")
            .GroupBy(x => x)
            .Select(g => new { channel = g.Key, sessions = g.Count() })
            .OrderByDescending(x => x.sessions)
            .ToList();
        var total = Math.Max(1, grouped.Sum(x => x.sessions));
        return grouped.Select(x => new AnalyticsListMetric(ToChannelLabel(x.channel), x.sessions, Math.Round((x.sessions * 100.0) / total, 1))).ToList();
    }

    private async Task EnsureTablesAsync(CancellationToken cancellationToken)
    {
        if (_ensured)
        {
            return;
        }

        await _ensureLock.WaitAsync(cancellationToken);
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
    City TEXT NULL,
    Language TEXT NULL,
    Browser TEXT NULL,
    Os TEXT NULL,
    Channel TEXT NULL,
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
    PageTitle TEXT NULL,
    LandingPath TEXT NULL,
    OccurredAtUtc TEXT NOT NULL,
    DurationMs INTEGER NULL,
    EngagementTimeMs INTEGER NULL,
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
    PageTitle TEXT NULL,
    OccurredAtUtc TEXT NOT NULL,
    CountryCode TEXT NOT NULL DEFAULT 'UNK',
    DeviceType TEXT NULL,
    ReferrerHost TEXT NULL,
    MetadataJson TEXT NULL,
    UtmSource TEXT NULL,
    UtmMedium TEXT NULL,
    UtmCampaign TEXT NULL,
    UtmTerm TEXT NULL,
    UtmContent TEXT NULL,
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
                await TryAlterSqliteAsync("ALTER TABLE AnalyticsSessions ADD COLUMN City TEXT NULL;");
                await TryAlterSqliteAsync("ALTER TABLE AnalyticsSessions ADD COLUMN Language TEXT NULL;");
                await TryAlterSqliteAsync("ALTER TABLE AnalyticsSessions ADD COLUMN Browser TEXT NULL;");
                await TryAlterSqliteAsync("ALTER TABLE AnalyticsSessions ADD COLUMN Os TEXT NULL;");
                await TryAlterSqliteAsync("ALTER TABLE AnalyticsSessions ADD COLUMN Channel TEXT NULL;");
                await TryAlterSqliteAsync("ALTER TABLE AnalyticsPageViews ADD COLUMN PageTitle TEXT NULL;");
                await TryAlterSqliteAsync("ALTER TABLE AnalyticsPageViews ADD COLUMN LandingPath TEXT NULL;");
                await TryAlterSqliteAsync("ALTER TABLE AnalyticsPageViews ADD COLUMN EngagementTimeMs INTEGER NULL;");
                await TryAlterSqliteAsync("ALTER TABLE AnalyticsEvents ADD COLUMN PageTitle TEXT NULL;");
                await TryAlterSqliteAsync("ALTER TABLE AnalyticsEvents ADD COLUMN UtmSource TEXT NULL;");
                await TryAlterSqliteAsync("ALTER TABLE AnalyticsEvents ADD COLUMN UtmMedium TEXT NULL;");
                await TryAlterSqliteAsync("ALTER TABLE AnalyticsEvents ADD COLUMN UtmCampaign TEXT NULL;");
                await TryAlterSqliteAsync("ALTER TABLE AnalyticsEvents ADD COLUMN UtmTerm TEXT NULL;");
                await TryAlterSqliteAsync("ALTER TABLE AnalyticsEvents ADD COLUMN UtmContent TEXT NULL;");
                await _db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_AnalyticsSessions_ChannelScope ON AnalyticsSessions(ProjectId, Channel, DeviceType, CountryCode, LastSeenUtc);");
                await _db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_AnalyticsPageViews_LandingPath ON AnalyticsPageViews(ProjectId, LandingPath, OccurredAtUtc);");
                await _db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_AnalyticsPageViews_PageTitle ON AnalyticsPageViews(ProjectId, PageTitle, OccurredAtUtc);");
                await _db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_AnalyticsEvents_EventType ON AnalyticsEvents(ProjectId, EventType, OccurredAtUtc);");
                await _db.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_AnalyticsEvents_Utm ON AnalyticsEvents(ProjectId, UtmSource, UtmMedium, OccurredAtUtc);");
                await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS AnalyticsCustomFunnels (
    Id TEXT NOT NULL PRIMARY KEY,
    ProjectId INTEGER NOT NULL,
    OwnerUserId TEXT NOT NULL,
    CompanyId TEXT NULL,
    Name TEXT NOT NULL,
    StepsJson TEXT NOT NULL,
    IsSystemPreset INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_AnalyticsCustomFunnels_Key ON AnalyticsCustomFunnels(OwnerUserId, CompanyId, ProjectId, Name);
CREATE INDEX IF NOT EXISTS IX_AnalyticsCustomFunnels_Updated ON AnalyticsCustomFunnels(OwnerUserId, CompanyId, ProjectId, UpdatedAtUtc);");
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
    City NVARCHAR(120) NULL,
    Language NVARCHAR(24) NULL,
    Browser NVARCHAR(64) NULL,
    Os NVARCHAR(64) NULL,
    Channel NVARCHAR(32) NULL,
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
    PageTitle NVARCHAR(1024) NULL,
    LandingPath NVARCHAR(1024) NULL,
    OccurredAtUtc DATETIME2 NOT NULL,
    DurationMs INT NULL,
    EngagementTimeMs INT NULL,
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
    PageTitle NVARCHAR(1024) NULL,
    OccurredAtUtc DATETIME2 NOT NULL,
    CountryCode NVARCHAR(8) NOT NULL CONSTRAINT DF_AnalyticsEvents_Country DEFAULT 'UNK',
    DeviceType NVARCHAR(16) NULL,
    ReferrerHost NVARCHAR(253) NULL,
    MetadataJson NVARCHAR(MAX) NULL,
    UtmSource NVARCHAR(160) NULL,
    UtmMedium NVARCHAR(160) NULL,
    UtmCampaign NVARCHAR(200) NULL,
    UtmTerm NVARCHAR(200) NULL,
    UtmContent NVARCHAR(200) NULL,
    OwnerUserId NVARCHAR(450) NULL,
    CompanyId UNIQUEIDENTIFIER NULL
);
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_AnalyticsEvents_Scope')
CREATE INDEX IX_AnalyticsEvents_Scope ON AnalyticsEvents(OwnerUserId, CompanyId, ProjectId, OccurredAtUtc);
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_AnalyticsEvents_Name')
CREATE INDEX IX_AnalyticsEvents_Name ON AnalyticsEvents(ProjectId, EventName, OccurredAtUtc);");

                await _db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('AnalyticsSessions', 'DeviceType') IS NULL
    ALTER TABLE AnalyticsSessions ADD DeviceType NVARCHAR(16) NULL;
IF COL_LENGTH('AnalyticsSessions', 'City') IS NULL
    ALTER TABLE AnalyticsSessions ADD City NVARCHAR(120) NULL;
IF COL_LENGTH('AnalyticsSessions', 'Language') IS NULL
    ALTER TABLE AnalyticsSessions ADD Language NVARCHAR(24) NULL;
IF COL_LENGTH('AnalyticsSessions', 'Browser') IS NULL
    ALTER TABLE AnalyticsSessions ADD Browser NVARCHAR(64) NULL;
IF COL_LENGTH('AnalyticsSessions', 'Os') IS NULL
    ALTER TABLE AnalyticsSessions ADD Os NVARCHAR(64) NULL;
IF COL_LENGTH('AnalyticsSessions', 'Channel') IS NULL
    ALTER TABLE AnalyticsSessions ADD Channel NVARCHAR(32) NULL;
IF COL_LENGTH('AnalyticsPageViews', 'PageTitle') IS NULL
    ALTER TABLE AnalyticsPageViews ADD PageTitle NVARCHAR(1024) NULL;
IF COL_LENGTH('AnalyticsPageViews', 'LandingPath') IS NULL
    ALTER TABLE AnalyticsPageViews ADD LandingPath NVARCHAR(1024) NULL;
IF COL_LENGTH('AnalyticsPageViews', 'EngagementTimeMs') IS NULL
    ALTER TABLE AnalyticsPageViews ADD EngagementTimeMs INT NULL;
IF COL_LENGTH('AnalyticsEvents', 'PageTitle') IS NULL
    ALTER TABLE AnalyticsEvents ADD PageTitle NVARCHAR(1024) NULL;
IF COL_LENGTH('AnalyticsEvents', 'UtmSource') IS NULL
    ALTER TABLE AnalyticsEvents ADD UtmSource NVARCHAR(160) NULL;
IF COL_LENGTH('AnalyticsEvents', 'UtmMedium') IS NULL
    ALTER TABLE AnalyticsEvents ADD UtmMedium NVARCHAR(160) NULL;
IF COL_LENGTH('AnalyticsEvents', 'UtmCampaign') IS NULL
    ALTER TABLE AnalyticsEvents ADD UtmCampaign NVARCHAR(200) NULL;
IF COL_LENGTH('AnalyticsEvents', 'UtmTerm') IS NULL
    ALTER TABLE AnalyticsEvents ADD UtmTerm NVARCHAR(200) NULL;
IF COL_LENGTH('AnalyticsEvents', 'UtmContent') IS NULL
    ALTER TABLE AnalyticsEvents ADD UtmContent NVARCHAR(200) NULL;
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_AnalyticsSessions_ChannelScope')
    CREATE INDEX IX_AnalyticsSessions_ChannelScope ON AnalyticsSessions(ProjectId, Channel, DeviceType, CountryCode, LastSeenUtc);
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_AnalyticsPageViews_LandingPath')
    CREATE INDEX IX_AnalyticsPageViews_LandingPath ON AnalyticsPageViews(ProjectId, LandingPath, OccurredAtUtc);
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_AnalyticsPageViews_PageTitle')
    CREATE INDEX IX_AnalyticsPageViews_PageTitle ON AnalyticsPageViews(ProjectId, PageTitle, OccurredAtUtc);
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_AnalyticsEvents_EventType')
    CREATE INDEX IX_AnalyticsEvents_EventType ON AnalyticsEvents(ProjectId, EventType, OccurredAtUtc);
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = 'IX_AnalyticsEvents_Utm')
    CREATE INDEX IX_AnalyticsEvents_Utm ON AnalyticsEvents(ProjectId, UtmSource, UtmMedium, OccurredAtUtc);");
                await _db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID('dbo.AnalyticsCustomFunnels','U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AnalyticsCustomFunnels](
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        [ProjectId] INT NOT NULL,
        [OwnerUserId] NVARCHAR(450) NOT NULL,
        [CompanyId] UNIQUEIDENTIFIER NULL,
        [Name] NVARCHAR(100) NOT NULL,
        [StepsJson] NVARCHAR(MAX) NOT NULL,
        [IsSystemPreset] BIT NOT NULL DEFAULT 0,
        [CreatedAtUtc] DATETIME2 NOT NULL,
        [UpdatedAtUtc] DATETIME2 NOT NULL
    );
END
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name='IX_AnalyticsCustomFunnels_Key')
    CREATE UNIQUE INDEX IX_AnalyticsCustomFunnels_Key ON AnalyticsCustomFunnels(OwnerUserId, CompanyId, ProjectId, Name);
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name='IX_AnalyticsCustomFunnels_Updated')
    CREATE INDEX IX_AnalyticsCustomFunnels_Updated ON AnalyticsCustomFunnels(OwnerUserId, CompanyId, ProjectId, UpdatedAtUtc);");
            }

            _ensured = true;
        }
        finally
        {
            _ensureLock.Release();
        }
    }

    private async Task TryAlterSqliteAsync(string sql)
    {
        try
        {
            await _db.Database.ExecuteSqlRawAsync(sql);
        }
        catch
        {
            // Column or index may already exist.
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

    private static string NormalizeTab(string? tab)
    {
        var normalized = (tab ?? "overview").Trim().ToLowerInvariant();
        return normalized is "overview" or "realtime" or "acquisition" or "engagement" or "audience" or "conversions" or "seo" or "pages"
            ? normalized
            : "overview";
    }

    private static string NormalizeSegment(string? segment)
    {
        var normalized = (segment ?? "all").Trim().ToLowerInvariant();
        return normalized is "all" or "paid" or "organic" or "direct" or "referral" ? normalized : "all";
    }

    private static string NormalizeChannel(string? channel)
    {
        var normalized = (channel ?? "direct").Trim().ToLowerInvariant();
        return normalized switch
        {
            "paid" => "paid",
            "organic" => "organic",
            "referral" => "referral",
            "direct" => "direct",
            "social" => "social",
            "email" => "email",
            _ => "direct"
        };
    }

    private static string ToChannelLabel(string channel) => channel switch
    {
        "organic" => "Organic Search",
        "paid" => "Paid",
        "referral" => "Referral",
        "social" => "Social",
        "email" => "Email",
        _ => "Direct"
    };

    private static string ResolveChannel(string? utmSource, string? utmMedium, string? referrerHost)
    {
        var medium = (utmMedium ?? string.Empty).Trim().ToLowerInvariant();
        var source = (utmSource ?? string.Empty).Trim().ToLowerInvariant();
        var referrer = (referrerHost ?? string.Empty).Trim().ToLowerInvariant();

        if (medium.Contains("cpc", StringComparison.Ordinal) || medium.Contains("ppc", StringComparison.Ordinal) || medium.Contains("paid", StringComparison.Ordinal))
        {
            return "paid";
        }
        if (medium.Contains("email", StringComparison.Ordinal))
        {
            return "email";
        }
        if (medium.Contains("social", StringComparison.Ordinal) || source.Contains("facebook", StringComparison.Ordinal) || source.Contains("instagram", StringComparison.Ordinal) || source.Contains("linkedin", StringComparison.Ordinal))
        {
            return "social";
        }
        if (medium.Contains("organic", StringComparison.Ordinal))
        {
            return "organic";
        }
        if (!string.IsNullOrWhiteSpace(referrer) && !string.Equals(referrer, "direct", StringComparison.OrdinalIgnoreCase))
        {
            return "referral";
        }
        return "direct";
    }

    private static string? NormalizeCity(string? city)
    {
        if (string.IsNullOrWhiteSpace(city))
        {
            return null;
        }
        var trimmed = city.Trim();
        return trimmed.Length > 120 ? trimmed[..120] : trimmed;
    }

    private static string? NormalizeLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var trimmed = value.Trim().ToLowerInvariant();
        return trimmed.Length > 24 ? trimmed[..24] : trimmed;
    }

    private static string? NormalizePageTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var trimmed = value.Trim();
        return trimmed.Length > 1024 ? trimmed[..1024] : trimmed;
    }

    private static string? NormalizeUtm(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var trimmed = value.Trim().ToLowerInvariant();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static string? NormalizeBrowser(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }
        var ua = userAgent.ToLowerInvariant();
        if (ua.Contains("edg/", StringComparison.Ordinal)) return "edge";
        if (ua.Contains("chrome/", StringComparison.Ordinal) && !ua.Contains("edg/", StringComparison.Ordinal)) return "chrome";
        if (ua.Contains("firefox/", StringComparison.Ordinal)) return "firefox";
        if (ua.Contains("safari/", StringComparison.Ordinal) && !ua.Contains("chrome/", StringComparison.Ordinal)) return "safari";
        if (ua.Contains("msie", StringComparison.Ordinal) || ua.Contains("trident", StringComparison.Ordinal)) return "ie";
        return "other";
    }

    private static string? NormalizeOs(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }
        var ua = userAgent.ToLowerInvariant();
        if (ua.Contains("windows", StringComparison.Ordinal)) return "windows";
        if (ua.Contains("android", StringComparison.Ordinal)) return "android";
        if (ua.Contains("iphone", StringComparison.Ordinal) || ua.Contains("ipad", StringComparison.Ordinal) || ua.Contains("ios", StringComparison.Ordinal)) return "ios";
        if (ua.Contains("mac os", StringComparison.Ordinal) || ua.Contains("macintosh", StringComparison.Ordinal)) return "macos";
        if (ua.Contains("linux", StringComparison.Ordinal)) return "linux";
        return "other";
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
