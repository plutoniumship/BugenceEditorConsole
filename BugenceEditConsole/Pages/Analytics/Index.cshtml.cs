using System.Globalization;
using System.Text;
using System.Text.Json;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Analytics;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAnalyticsQueryService _analytics;
    private readonly IGoogleSearchConsoleService _searchConsole;

    public IndexModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAnalyticsQueryService analytics,
        IGoogleSearchConsoleService searchConsole)
    {
        _db = db;
        _userManager = userManager;
        _analytics = analytics;
        _searchConsole = searchConsole;
    }

    public sealed record AnalyticsProjectOption(int Id, string Name, string DomainName);

    public IReadOnlyList<AnalyticsProjectOption> Projects { get; private set; } = Array.Empty<AnalyticsProjectOption>();
    public bool HasEligibleProjects { get; private set; }
    public int SelectedProjectId { get; private set; }
    public string SelectedProjectName { get; private set; } = "Project";
    public string SelectedDomainName { get; private set; } = string.Empty;
    public string SelectedRange { get; private set; } = "30d";
    public string SelectedTab { get; private set; } = "overview";
    public bool CompareEnabled { get; private set; }
    public string SelectedSegment { get; private set; } = "all";
    public AnalyticsQueryFilters ActiveFilters { get; private set; } = new(null, null, null, null, null, null, null);

    public AnalyticsSnapshot Snapshot { get; private set; } = AnalyticsSnapshot.Empty(0, "30d");
    public IReadOnlyList<AnalyticsReferrerSummary> TopReferrers { get; private set; } = Array.Empty<AnalyticsReferrerSummary>();
    public IReadOnlyList<CountryView> TopCountries { get; private set; } = Array.Empty<CountryView>();
    public IReadOnlyList<AnalyticsReferrerSummary> ConversionReferrers { get; private set; } = Array.Empty<AnalyticsReferrerSummary>();
    public IReadOnlyList<CountryView> ConversionCountries { get; private set; } = Array.Empty<CountryView>();
    public IReadOnlyList<AnalyticsDeviceConversionSummary> ConversionDevices { get; private set; } = Array.Empty<AnalyticsDeviceConversionSummary>();
    public DeviceBreakdownView Devices { get; private set; } = new(0, 0, 0, 0, 0, 0, 0, 0);
    public TrackingReadinessView Readiness { get; private set; } = new(false, false, false, false, false, null, "Connect a custom domain with active SSL to start analytics.");

    public double UniqueVisitorsDeltaPercent { get; private set; }
    public double PageviewsDeltaPercent { get; private set; }
    public double AvgSessionDeltaPercent { get; private set; }
    public double BounceRateDeltaPercent { get; private set; }
    public double ConversionDeltaPercent { get; private set; }
    public double ConversionRateDeltaPercent { get; private set; }
    public double LeadsPer100DeltaPercent { get; private set; }

    public sealed record CountryView(string Code, string Label, int Visits, double Percent);
    public sealed record DeviceBreakdownView(
        int Desktop,
        int Mobile,
        int Tablet,
        int Other,
        double DesktopPercent,
        double MobilePercent,
        double TabletPercent,
        double OtherPercent);
    public sealed record TrackingReadinessView(
        bool DomainConnected,
        bool SslActive,
        bool HasTraffic,
        bool HasConversions,
        bool TrackerLikelyActive,
        DateTime? LastEventSeenUtc,
        string StatusText);

    public async Task<IActionResult> OnGetAsync(
        int? projectId,
        string? range,
        string? tab,
        string? compare,
        string? segment,
        string? country,
        string? device,
        string? landingPage,
        string? referrer,
        string? utmSource,
        string? utmMedium,
        string? utmCampaign)
    {
        await EnsureSavedViewsTableAsync(HttpContext.RequestAborted);
        await EnsureCustomFunnelsTableAsync(HttpContext.RequestAborted);

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Page();
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        SelectedRange = NormalizeRange(range);
        SelectedTab = NormalizeTab(tab);
        CompareEnabled = NormalizeCompare(compare);
        SelectedSegment = NormalizeSegment(segment);
        ActiveFilters = new AnalyticsQueryFilters(country, device, landingPage, referrer, utmSource, utmMedium, utmCampaign);

        Projects = await LoadEligibleProjectsAsync(accessScope, HttpContext.RequestAborted);
        HasEligibleProjects = Projects.Count > 0;

        if (!HasEligibleProjects)
        {
            Snapshot = AnalyticsSnapshot.Empty(0, SelectedRange);
            return Page();
        }

        var selected = projectId.HasValue
            ? Projects.FirstOrDefault(p => p.Id == projectId.Value)
            : Projects[0];

        if (selected == null)
        {
            selected = Projects[0];
        }

        SelectedProjectId = selected.Id;
        SelectedProjectName = selected.Name;
        SelectedDomainName = selected.DomainName;
        await SeedSystemSavedViewsAsync(accessScope, SelectedProjectId, HttpContext.RequestAborted);
        await SeedDefaultFunnelAsync(accessScope, SelectedProjectId, HttpContext.RequestAborted);

        Snapshot = await _analytics.GetProjectSnapshotAsync(
            accessScope.OwnerUserId,
            accessScope.CompanyId,
            SelectedProjectId,
            SelectedRange,
            HttpContext.RequestAborted);

        TopReferrers = Snapshot.TopReferrers;
        TopCountries = Snapshot.TopCountries
            .Select(c => new CountryView(c.CountryCode, CountryLabel(c.CountryCode), c.Visits, c.Percent))
            .ToList();
        ConversionReferrers = Snapshot.ConversionByReferrers;
        ConversionCountries = Snapshot.ConversionByCountries
            .Select(c => new CountryView(c.CountryCode, CountryLabel(c.CountryCode), c.Visits, c.Percent))
            .ToList();
        ConversionDevices = Snapshot.ConversionByDevices;
        Devices = BuildDeviceBreakdown(Snapshot.Devices);
        Readiness = BuildReadiness(selected.DomainName, Snapshot);

        UniqueVisitorsDeltaPercent = PercentChange(Snapshot.Current.UniqueVisitors, Snapshot.Previous.UniqueVisitors);
        PageviewsDeltaPercent = PercentChange(Snapshot.Current.Pageviews, Snapshot.Previous.Pageviews);
        AvgSessionDeltaPercent = PercentChange(Snapshot.Current.AvgSession.TotalSeconds, Snapshot.Previous.AvgSession.TotalSeconds);
        BounceRateDeltaPercent = PercentChange(Snapshot.Current.BounceRatePercent, Snapshot.Previous.BounceRatePercent);
        ConversionDeltaPercent = PercentChange(Snapshot.ConversionCount, Snapshot.PreviousConversionCount);
        ConversionRateDeltaPercent = PercentChange(Snapshot.ConversionRatePercent, Snapshot.PreviousConversionRatePercent);
        LeadsPer100DeltaPercent = PercentChange(Snapshot.LeadsPer100Sessions, Snapshot.PreviousLeadsPer100Sessions);

        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync(int projectId, string? range)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        var options = await LoadEligibleProjectsAsync(accessScope, HttpContext.RequestAborted);
        var selected = options.FirstOrDefault(p => p.Id == projectId);
        if (selected == null)
        {
            return BadRequest("Selected project is not eligible for analytics.");
        }

        var normalizedRange = NormalizeRange(range);
        var snapshot = await _analytics.GetProjectSnapshotAsync(
            accessScope.OwnerUserId,
            accessScope.CompanyId,
            selected.Id,
            normalizedRange,
            HttpContext.RequestAborted);

        var csv = BuildCsv(selected, snapshot);
        var fileName = $"analytics-{selected.Id}-{normalizedRange}-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", fileName);
    }

    public async Task<IActionResult> OnGetLiveAsync(
        int projectId,
        string? range,
        string? tab,
        string? compare,
        string? segment,
        string? module,
        string? dimension,
        string? funnelMode,
        Guid? funnelId,
        string? country,
        string? device,
        string? landingPage,
        string? referrer,
        string? utmSource,
        string? utmMedium,
        string? utmCampaign)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 401 };
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        var options = await LoadEligibleProjectsAsync(accessScope, HttpContext.RequestAborted);
        if (!options.Any(p => p.Id == projectId))
        {
            return new JsonResult(new { success = false, message = "Project not eligible" }) { StatusCode = 400 };
        }

        var normalizedRange = NormalizeRange(range);
        var snapshot = await _analytics.GetProjectSnapshotAsync(
            accessScope.OwnerUserId,
            accessScope.CompanyId,
            projectId,
            normalizedRange,
            HttpContext.RequestAborted);
        var tabPayload = await _analytics.GetTabPayloadAsync(
            accessScope.OwnerUserId,
            accessScope.CompanyId,
            projectId,
            normalizedRange,
            new AnalyticsTabQuery(
                Tab: NormalizeTab(tab),
                Compare: NormalizeCompare(compare),
                Segment: NormalizeSegment(segment),
                Filters: new AnalyticsQueryFilters(country, device, landingPage, referrer, utmSource, utmMedium, utmCampaign),
                Module: module,
                Dimension: dimension,
                FunnelMode: funnelMode,
                FunnelId: funnelId),
            HttpContext.RequestAborted);
        var deviceBreakdown = BuildDeviceBreakdown(snapshot.Devices);
        var readiness = BuildReadiness(options.First(p => p.Id == projectId).DomainName, snapshot);

        return new JsonResult(new
        {
            success = true,
            generatedAtUtc = snapshot.GeneratedAtUtc,
            uniqueVisitors = snapshot.Current.UniqueVisitors,
            pageviews = snapshot.Current.Pageviews,
            sessions = snapshot.Current.Sessions,
            avgSessionSeconds = snapshot.Current.AvgSession.TotalSeconds,
            bounceRate = snapshot.Current.BounceRatePercent,
            conversions = snapshot.ConversionCount,
            conversionRate = snapshot.ConversionRatePercent,
            leadsPer100 = snapshot.LeadsPer100Sessions,
            activeUsers = snapshot.ActiveUsersNow,
            pageviewsPerMinute = snapshot.PageviewsPerMinute,
            uniqueVisitorsDelta = PercentChange(snapshot.Current.UniqueVisitors, snapshot.Previous.UniqueVisitors),
            avgSessionDelta = PercentChange(snapshot.Current.AvgSession.TotalSeconds, snapshot.Previous.AvgSession.TotalSeconds),
            bounceRateDelta = PercentChange(snapshot.Current.BounceRatePercent, snapshot.Previous.BounceRatePercent),
            conversionDelta = PercentChange(snapshot.ConversionCount, snapshot.PreviousConversionCount),
            conversionRateDelta = PercentChange(snapshot.ConversionRatePercent, snapshot.PreviousConversionRatePercent),
            leadsPer100Delta = PercentChange(snapshot.LeadsPer100Sessions, snapshot.PreviousLeadsPer100Sessions),
            lastEventSeenUtc = snapshot.LastEventSeenUtc,
            trafficSeries = normalizedRange switch
            {
                "24h" => snapshot.Traffic24h,
                "7d" => snapshot.Traffic7d,
                _ => snapshot.Traffic30d
            },
            topPages = snapshot.TopPages
                .Take(8)
                .Select(x => new { path = x.Path, visits = x.Visits, percent = x.Percent }),
            topReferrers = snapshot.TopReferrers
                .Take(8)
                .Select(x => new { host = x.Host, visits = x.Visits }),
            topCountries = snapshot.TopCountries
                .Take(8)
                .Select(x => new
                {
                    code = x.CountryCode,
                    label = CountryLabel(x.CountryCode),
                    visits = x.Visits,
                    percent = x.Percent
                }),
            funnel = new
            {
                overallConversionRate = snapshot.Funnel.OverallConversionRatePercent,
                steps = snapshot.Funnel.Steps.Select(step => new
                {
                    key = step.Key,
                    label = step.Label,
                    sessions = step.Sessions,
                    dropoffCount = step.DropoffCount,
                    dropoffPercent = step.DropoffPercent
                })
            },
            sources = new
            {
                naPercent = snapshot.Sources.NaPercent,
                euPercent = snapshot.Sources.EuPercent,
                asiaPercent = snapshot.Sources.AsiaPercent
            },
            conversionReferrers = snapshot.ConversionByReferrers
                .Take(8)
                .Select(x => new { host = x.Host, visits = x.Visits, percent = x.Percent }),
            conversionCountries = snapshot.ConversionByCountries
                .Take(8)
                .Select(x => new
                {
                    code = x.CountryCode,
                    label = CountryLabel(x.CountryCode),
                    visits = x.Visits,
                    percent = x.Percent
                }),
            conversionDevices = snapshot.ConversionByDevices
                .Take(8)
                .Select(x => new
                {
                    deviceType = x.DeviceType,
                    label = DeviceLabel(x.DeviceType),
                    conversions = x.Conversions,
                    percent = x.Percent
                }),
            devices = new
            {
                desktop = snapshot.Devices.Desktop,
                mobile = snapshot.Devices.Mobile,
                tablet = snapshot.Devices.Tablet,
                other = snapshot.Devices.Other,
                desktopPercent = deviceBreakdown.DesktopPercent,
                mobilePercent = deviceBreakdown.MobilePercent,
                tabletPercent = deviceBreakdown.TabletPercent,
                otherPercent = deviceBreakdown.OtherPercent
            },
            readiness = new
            {
                domainConnected = true,
                sslActive = true,
                hasTraffic = snapshot.Current.Pageviews > 0 || snapshot.Current.Sessions > 0,
                hasConversions = snapshot.ConversionCount > 0,
                trackerLikelyActive = snapshot.Current.Pageviews > 0,
                statusText = readiness.StatusText
            },
            tabPayload,
            contractVersion = "v2"
        });
    }

    public async Task<IActionResult> OnGetTabAsync(
        int projectId,
        string? range,
        string? tab,
        string? compare,
        string? segment,
        string? module,
        string? dimension,
        string? funnelMode,
        Guid? funnelId,
        string? country,
        string? device,
        string? landingPage,
        string? referrer,
        string? utmSource,
        string? utmMedium,
        string? utmCampaign,
        int page = 1,
        int pageSize = 25,
        string? search = null,
        string? sortBy = null,
        string? sortDir = null)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 401 };
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        var options = await LoadEligibleProjectsAsync(accessScope, HttpContext.RequestAborted);
        if (!options.Any(p => p.Id == projectId))
        {
            return new JsonResult(new { success = false, message = "Project not eligible" }) { StatusCode = 400 };
        }

        var payload = await _analytics.GetTabPayloadAsync(
            accessScope.OwnerUserId,
            accessScope.CompanyId,
            projectId,
            NormalizeRange(range),
            new AnalyticsTabQuery(
                Tab: NormalizeTab(tab),
                Compare: NormalizeCompare(compare),
                Segment: NormalizeSegment(segment),
                Filters: new AnalyticsQueryFilters(country, device, landingPage, referrer, utmSource, utmMedium, utmCampaign),
                Page: Math.Max(1, page),
                PageSize: Math.Clamp(pageSize, 1, 100),
                Search: search,
                SortBy: sortBy,
                SortDir: sortDir,
                Dimension: dimension,
                Module: module,
                FunnelMode: funnelMode,
                FunnelId: funnelId),
            HttpContext.RequestAborted);

        return new JsonResult(new { success = true, tab = payload, contractVersion = "v2" });
    }

    public async Task<IActionResult> OnGetSeoConnectorStatusAsync(int projectId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 401 };
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        var options = await LoadEligibleProjectsAsync(accessScope, HttpContext.RequestAborted);
        if (!options.Any(p => p.Id == projectId))
        {
            return new JsonResult(new { success = false, message = "Project not eligible" }) { StatusCode = 400 };
        }

        var status = await _searchConsole.GetStatusAsync(accessScope.OwnerUserId, accessScope.CompanyId, projectId, HttpContext.RequestAborted);
        return new JsonResult(new
        {
            success = true,
            connected = status.Connected,
            lastSyncUtc = status.LastSyncUtc,
            selectedProperty = status.SelectedProperty,
            authScopeState = status.AuthScopeState,
            hasCachedData = status.HasCachedData
        });
    }

    public async Task<IActionResult> OnPostSeoConnectAsync(int projectId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 401 };
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        var options = await LoadEligibleProjectsAsync(accessScope, HttpContext.RequestAborted);
        if (!options.Any(p => p.Id == projectId))
        {
            return new JsonResult(new { success = false, message = "Project not eligible" }) { StatusCode = 400 };
        }
        var returnUrl = Url.Page("/Analytics/Index", null, new { projectId, tab = "seo" }, Request.Scheme)
            ?? $"/Analytics/Index?projectId={projectId}&tab=seo";
        var connectUrl = $"/Auth/ExternalConnect?provider=Google&returnUrl={Uri.EscapeDataString(returnUrl)}";
        return new JsonResult(new { success = true, connectUrl });
    }

    public async Task<IActionResult> OnGetSeoPropertiesAsync(int projectId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 401 };
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        var options = await LoadEligibleProjectsAsync(accessScope, HttpContext.RequestAborted);
        if (!options.Any(p => p.Id == projectId))
        {
            return new JsonResult(new { success = false, message = "Project not eligible" }) { StatusCode = 400 };
        }

        var properties = await _searchConsole.GetPropertiesAsync(accessScope.OwnerUserId, accessScope.CompanyId, HttpContext.RequestAborted);
        return new JsonResult(new { success = true, properties });
    }

    public async Task<IActionResult> OnPostSeoSelectPropertyAsync(int projectId, string propertyUri)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 401 };
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        await _searchConsole.SetSelectedPropertyAsync(accessScope.OwnerUserId, accessScope.CompanyId, projectId, propertyUri, HttpContext.RequestAborted);
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostSeoSyncAsync(int projectId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 401 };
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        var ok = await _searchConsole.SyncAsync(accessScope.OwnerUserId, accessScope.CompanyId, projectId, HttpContext.RequestAborted);
        return new JsonResult(new { success = ok });
    }

    public async Task<IActionResult> OnGetSeoReportAsync(int projectId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 401 };
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        var issues = await _searchConsole.GetLatestSnapshotAsync(accessScope.OwnerUserId, accessScope.CompanyId, projectId, "issues", HttpContext.RequestAborted);
        var sitemaps = await _searchConsole.GetLatestSnapshotAsync(accessScope.OwnerUserId, accessScope.CompanyId, projectId, "sitemaps", HttpContext.RequestAborted);
        var performance = await _searchConsole.GetLatestSnapshotAsync(accessScope.OwnerUserId, accessScope.CompanyId, projectId, "search_performance", HttpContext.RequestAborted);
        return new JsonResult(new
        {
            success = true,
            issues,
            sitemaps,
            performance
        });
    }

    public async Task<IActionResult> OnGetSavedViewsAsync(int projectId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 401 };
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        await SeedSystemSavedViewsAsync(accessScope, projectId, HttpContext.RequestAborted);

        var query = _db.AnalyticsSavedViews.AsNoTracking().Where(x => x.ProjectId == projectId && x.OwnerUserId == accessScope.OwnerUserId);
        if (accessScope.CompanyId.HasValue)
        {
            query = query.Where(x => x.CompanyId == accessScope.CompanyId.Value);
        }

        var views = await query
            .OrderByDescending(x => x.IsSystemPreset)
            .ThenBy(x => x.Name)
            .Select(x => new
            {
                id = x.Id,
                name = x.Name,
                tab = x.Tab,
                range = x.Range,
                compare = x.Compare,
                segment = x.Segment,
                filtersJson = x.FiltersJson,
                isSystemPreset = x.IsSystemPreset
            })
            .ToListAsync(HttpContext.RequestAborted);

        return new JsonResult(new { success = true, views });
    }

    public async Task<IActionResult> OnPostSaveViewAsync(
        int projectId,
        string name,
        string? tab,
        string? range,
        string? compare,
        string? segment,
        string? country,
        string? device,
        string? landingPage,
        string? referrer,
        string? utmSource,
        string? utmMedium,
        string? utmCampaign)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 401 };
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        var viewName = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(viewName) || viewName.Length > 80)
        {
            return new JsonResult(new { success = false, message = "Invalid view name." }) { StatusCode = 400 };
        }

        if (string.Equals(viewName, "Executive", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(viewName, "SEO", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(viewName, "Ads", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(viewName, "Sales funnel", StringComparison.OrdinalIgnoreCase))
        {
            return new JsonResult(new { success = false, message = "Preset names are reserved." }) { StatusCode = 400 };
        }

        var existing = await _db.AnalyticsSavedViews.FirstOrDefaultAsync(x =>
            x.ProjectId == projectId &&
            x.OwnerUserId == accessScope.OwnerUserId &&
            x.Name == viewName,
            HttpContext.RequestAborted);

        if (existing is null)
        {
            existing = new AnalyticsSavedView
            {
                ProjectId = projectId,
                OwnerUserId = accessScope.OwnerUserId,
                CompanyId = accessScope.CompanyId,
                Name = viewName
            };
            _db.AnalyticsSavedViews.Add(existing);
        }

        existing.Tab = NormalizeTab(tab);
        existing.Range = NormalizeRange(range);
        existing.Compare = NormalizeCompare(compare);
        existing.Segment = NormalizeSegment(segment);
        existing.FiltersJson = JsonSerializer.Serialize(new
        {
            country,
            device,
            landingPage,
            referrer,
            utmSource,
            utmMedium,
            utmCampaign
        });
        existing.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return new JsonResult(new { success = true, id = existing.Id });
    }

    public async Task<IActionResult> OnPostApplyViewAsync(Guid viewId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 401 };
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        var view = await _db.AnalyticsSavedViews.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == viewId && x.OwnerUserId == accessScope.OwnerUserId, HttpContext.RequestAborted);
        if (view is null)
        {
            return new JsonResult(new { success = false, message = "Saved view not found." }) { StatusCode = 404 };
        }

        return new JsonResult(new
        {
            success = true,
            view = new
            {
                id = view.Id,
                name = view.Name,
                tab = view.Tab,
                range = view.Range,
                compare = view.Compare,
                segment = view.Segment,
                filtersJson = view.FiltersJson,
                isSystemPreset = view.IsSystemPreset
            }
        });
    }

    public async Task<IActionResult> OnPostDeleteViewAsync(Guid viewId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 401 };
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        var row = await _db.AnalyticsSavedViews.FirstOrDefaultAsync(x => x.Id == viewId && x.OwnerUserId == accessScope.OwnerUserId, HttpContext.RequestAborted);
        if (row is null)
        {
            return new JsonResult(new { success = false, message = "Saved view not found." }) { StatusCode = 404 };
        }

        if (row.IsSystemPreset)
        {
            return new JsonResult(new { success = false, message = "System presets cannot be deleted." }) { StatusCode = 400 };
        }

        _db.AnalyticsSavedViews.Remove(row);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnGetFunnelsAsync(int projectId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 401 };
        }

        await EnsureCustomFunnelsTableAsync(HttpContext.RequestAborted);
        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        await SeedDefaultFunnelAsync(accessScope, projectId, HttpContext.RequestAborted);

        var query = _db.AnalyticsCustomFunnels.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.OwnerUserId == accessScope.OwnerUserId);
        if (accessScope.CompanyId.HasValue)
        {
            query = query.Where(x => x.CompanyId == accessScope.CompanyId.Value);
        }

        var rows = await query.OrderByDescending(x => x.IsSystemPreset).ThenBy(x => x.Name)
            .Select(x => new { id = x.Id, name = x.Name, stepsJson = x.StepsJson, isSystemPreset = x.IsSystemPreset })
            .ToListAsync(HttpContext.RequestAborted);
        return new JsonResult(new { success = true, funnels = rows });
    }

    public async Task<IActionResult> OnPostSaveFunnelAsync(int projectId, string name, string stepsJson)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 401 };
        }

        await EnsureCustomFunnelsTableAsync(HttpContext.RequestAborted);
        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        var safeName = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(safeName) || safeName.Length > 100)
        {
            return new JsonResult(new { success = false, message = "Invalid funnel name." }) { StatusCode = 400 };
        }

        var existing = await _db.AnalyticsCustomFunnels.FirstOrDefaultAsync(x =>
            x.ProjectId == projectId &&
            x.OwnerUserId == accessScope.OwnerUserId &&
            x.Name == safeName, HttpContext.RequestAborted);
        if (existing is null)
        {
            existing = new AnalyticsCustomFunnel
            {
                ProjectId = projectId,
                OwnerUserId = accessScope.OwnerUserId,
                CompanyId = accessScope.CompanyId,
                Name = safeName
            };
            _db.AnalyticsCustomFunnels.Add(existing);
        }
        existing.StepsJson = string.IsNullOrWhiteSpace(stepsJson) ? "[]" : stepsJson;
        existing.IsSystemPreset = false;
        existing.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return new JsonResult(new { success = true, id = existing.Id });
    }

    public async Task<IActionResult> OnPostDeleteFunnelAsync(Guid funnelId)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Unauthorized" }) { StatusCode = 401 };
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        var row = await _db.AnalyticsCustomFunnels.FirstOrDefaultAsync(x =>
            x.Id == funnelId && x.OwnerUserId == accessScope.OwnerUserId, HttpContext.RequestAborted);
        if (row is null)
        {
            return new JsonResult(new { success = false, message = "Funnel not found." }) { StatusCode = 404 };
        }
        if (row.IsSystemPreset)
        {
            return new JsonResult(new { success = false, message = "System funnels cannot be deleted." }) { StatusCode = 400 };
        }

        _db.AnalyticsCustomFunnels.Remove(row);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        return new JsonResult(new { success = true });
    }

    private async Task SeedSystemSavedViewsAsync(CompanyAccessScopeResolver.Scope scope, int projectId, CancellationToken cancellationToken)
    {
        var presets = new[] { "Executive", "SEO", "Ads", "Sales funnel" };
        foreach (var preset in presets)
        {
            var exists = await _db.AnalyticsSavedViews.AnyAsync(x => x.ProjectId == projectId && x.OwnerUserId == scope.OwnerUserId && x.Name == preset, cancellationToken);
            if (exists)
            {
                continue;
            }

            var tab = preset switch
            {
                "SEO" => "seo",
                "Ads" => "acquisition",
                "Sales funnel" => "conversions",
                _ => "overview"
            };

            _db.AnalyticsSavedViews.Add(new AnalyticsSavedView
            {
                ProjectId = projectId,
                OwnerUserId = scope.OwnerUserId,
                CompanyId = scope.CompanyId,
                Name = preset,
                Tab = tab,
                Range = "30d",
                Compare = false,
                Segment = "all",
                FiltersJson = "{}",
                IsSystemPreset = true,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureSavedViewsTableAsync(CancellationToken cancellationToken)
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS AnalyticsSavedViews (
    Id TEXT NOT NULL PRIMARY KEY,
    ProjectId INTEGER NOT NULL,
    OwnerUserId TEXT NOT NULL,
    CompanyId TEXT NULL,
    Name TEXT NOT NULL,
    Tab TEXT NOT NULL,
    Range TEXT NOT NULL,
    Compare INTEGER NOT NULL DEFAULT 0,
    Segment TEXT NOT NULL,
    FiltersJson TEXT NOT NULL,
    IsSystemPreset INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_AnalyticsSavedViews_Key ON AnalyticsSavedViews(OwnerUserId, CompanyId, ProjectId, Name);
CREATE INDEX IF NOT EXISTS IX_AnalyticsSavedViews_Updated ON AnalyticsSavedViews(OwnerUserId, CompanyId, ProjectId, UpdatedAtUtc);");
        }
        else
        {
            await _db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID('dbo.AnalyticsSavedViews','U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AnalyticsSavedViews](
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        [ProjectId] INT NOT NULL,
        [OwnerUserId] NVARCHAR(450) NOT NULL,
        [CompanyId] UNIQUEIDENTIFIER NULL,
        [Name] NVARCHAR(80) NOT NULL,
        [Tab] NVARCHAR(32) NOT NULL,
        [Range] NVARCHAR(8) NOT NULL,
        [Compare] BIT NOT NULL DEFAULT 0,
        [Segment] NVARCHAR(24) NOT NULL,
        [FiltersJson] NVARCHAR(4000) NOT NULL,
        [IsSystemPreset] BIT NOT NULL DEFAULT 0,
        [CreatedAtUtc] DATETIME2 NOT NULL,
        [UpdatedAtUtc] DATETIME2 NOT NULL
    );
END
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name='IX_AnalyticsSavedViews_Key')
    CREATE UNIQUE INDEX IX_AnalyticsSavedViews_Key ON AnalyticsSavedViews(OwnerUserId, CompanyId, ProjectId, Name);
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name='IX_AnalyticsSavedViews_Updated')
    CREATE INDEX IX_AnalyticsSavedViews_Updated ON AnalyticsSavedViews(OwnerUserId, CompanyId, ProjectId, UpdatedAtUtc);");
        }
    }

    private async Task EnsureCustomFunnelsTableAsync(CancellationToken cancellationToken)
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
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
    }

    private async Task SeedDefaultFunnelAsync(CompanyAccessScopeResolver.Scope scope, int projectId, CancellationToken cancellationToken)
    {
        var exists = await _db.AnalyticsCustomFunnels.AnyAsync(x =>
            x.ProjectId == projectId &&
            x.OwnerUserId == scope.OwnerUserId &&
            x.Name == "Default Funnel", cancellationToken);
        if (exists)
        {
            return;
        }

        var steps = JsonSerializer.Serialize(new[]
        {
            new { type = "page", key = "/"},
            new { type = "page", key = "/services"},
            new { type = "page", key = "/contact"},
            new { type = "event", key = "form_submit:contact_form_submit"}
        });
        _db.AnalyticsCustomFunnels.Add(new AnalyticsCustomFunnel
        {
            ProjectId = projectId,
            OwnerUserId = scope.OwnerUserId,
            CompanyId = scope.CompanyId,
            Name = "Default Funnel",
            StepsJson = steps,
            IsSystemPreset = true,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<AnalyticsProjectOption>> LoadEligibleProjectsAsync(
        CompanyAccessScopeResolver.Scope scope,
        CancellationToken cancellationToken)
    {
        var projectsQuery = _db.UploadedProjects.AsNoTracking();
        if (scope.CompanyId.HasValue)
        {
            projectsQuery = projectsQuery.Where(p => p.CompanyId == scope.CompanyId.Value);
        }
        else
        {
            projectsQuery = projectsQuery.Where(p => p.UserId == scope.OwnerUserId);
        }

        var eligibleRows = await (
            from p in projectsQuery
            join d in _db.ProjectDomains.AsNoTracking() on p.Id equals d.UploadedProjectId
            where d.DomainType == ProjectDomainType.Custom
                && d.Status == DomainStatus.Connected
                && d.SslStatus == DomainSslStatus.Active
            select new
            {
                p.Id,
                Name = string.IsNullOrWhiteSpace(p.DisplayName) ? p.FolderName : p.DisplayName!,
                d.DomainName,
                d.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return eligibleRows
            .GroupBy(x => new { x.Id, x.Name })
            .Select(g =>
            {
                var domain = g
                    .OrderByDescending(x => x.UpdatedAtUtc)
                    .Select(x => x.DomainName)
                    .First();
                return new AnalyticsProjectOption(g.Key.Id, g.Key.Name, domain);
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildCsv(AnalyticsProjectOption selected, AnalyticsSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Section,Label,Value,Extra");
        sb.AppendLine($"Metadata,Project,{Csv(selected.Name)},");
        sb.AppendLine($"Metadata,Domain,{Csv(selected.DomainName)},");
        sb.AppendLine($"Metadata,Range,{Csv(snapshot.Range)},");
        sb.AppendLine($"Metadata,GeneratedUtc,{Csv(snapshot.GeneratedAtUtc.ToString("O"))},");

        sb.AppendLine($"KPI,Unique Visitors,{snapshot.Current.UniqueVisitors},{snapshot.Previous.UniqueVisitors}");
        sb.AppendLine($"KPI,Pageviews,{snapshot.Current.Pageviews},{snapshot.Previous.Pageviews}");
        sb.AppendLine($"KPI,Sessions,{snapshot.Current.Sessions},{snapshot.Previous.Sessions}");
        sb.AppendLine($"KPI,Avg Session Seconds,{snapshot.Current.AvgSession.TotalSeconds:0.##},{snapshot.Previous.AvgSession.TotalSeconds:0.##}");
        sb.AppendLine($"KPI,Bounce Rate %, {snapshot.Current.BounceRatePercent:0.##},{snapshot.Previous.BounceRatePercent:0.##}");
        sb.AppendLine($"KPI,Conversions,{snapshot.ConversionCount},{snapshot.PreviousConversionCount}");
        sb.AppendLine($"KPI,Conversion Rate %, {snapshot.ConversionRatePercent:0.##},{snapshot.PreviousConversionRatePercent:0.##}");
        sb.AppendLine($"KPI,Leads Per 100 Sessions,{snapshot.LeadsPer100Sessions:0.##},{snapshot.PreviousLeadsPer100Sessions:0.##}");
        sb.AppendLine($"KPI,Active Users Now,{snapshot.ActiveUsersNow},");
        sb.AppendLine($"KPI,Pageviews Per Minute,{snapshot.PageviewsPerMinute},");

        for (var i = 0; i < snapshot.Traffic24h.Count; i++)
        {
            sb.AppendLine($"Trend24h,Point {i + 1},{snapshot.Traffic24h[i]:0.##},");
        }

        for (var i = 0; i < snapshot.Traffic7d.Count; i++)
        {
            sb.AppendLine($"Trend7d,Point {i + 1},{snapshot.Traffic7d[i]:0.##},");
        }

        for (var i = 0; i < snapshot.Traffic30d.Count; i++)
        {
            sb.AppendLine($"Trend30d,Point {i + 1},{snapshot.Traffic30d[i]:0.##},");
        }

        foreach (var page in snapshot.TopPages)
        {
            sb.AppendLine($"TopPages,{Csv(page.Path)},{page.Visits},{page.Percent:0.##}%");
        }

        foreach (var referrer in snapshot.TopReferrers)
        {
            sb.AppendLine($"Referrers,{Csv(referrer.Host)},{referrer.Visits},{referrer.Percent:0.##}%");
        }

        foreach (var country in snapshot.TopCountries)
        {
            sb.AppendLine($"Countries,{Csv(country.CountryCode)},{country.Visits},{country.Percent:0.##}%");
        }

        foreach (var step in snapshot.Funnel.Steps)
        {
            sb.AppendLine($"Funnel,{Csv(step.Label)},{step.Sessions},Dropoff:{step.DropoffCount} ({step.DropoffPercent:0.##}%)");
        }
        sb.AppendLine($"Funnel,Overall Conversion %, {snapshot.Funnel.OverallConversionRatePercent:0.##},");

        foreach (var referrer in snapshot.ConversionByReferrers)
        {
            sb.AppendLine($"ConversionReferrers,{Csv(referrer.Host)},{referrer.Visits},{referrer.Percent:0.##}%");
        }
        foreach (var country in snapshot.ConversionByCountries)
        {
            sb.AppendLine($"ConversionCountries,{Csv(country.CountryCode)},{country.Visits},{country.Percent:0.##}%");
        }
        foreach (var device in snapshot.ConversionByDevices)
        {
            sb.AppendLine($"ConversionDevices,{Csv(device.DeviceType)},{device.Conversions},{device.Percent:0.##}%");
        }

        sb.AppendLine($"Devices,Desktop,{snapshot.Devices.Desktop},");
        sb.AppendLine($"Devices,Mobile,{snapshot.Devices.Mobile},");
        sb.AppendLine($"Devices,Tablet,{snapshot.Devices.Tablet},");
        sb.AppendLine($"Devices,Other,{snapshot.Devices.Other},");

        return sb.ToString();
    }

    private static string Csv(string value)
    {
        var safe = (value ?? string.Empty).Replace("\"", "\"\"");
        return $"\"{safe}\"";
    }

    private static string NormalizeRange(string? range)
    {
        var normalized = (range ?? "30d").Trim().ToLowerInvariant();
        return normalized is "24h" or "7d" or "30d" ? normalized : "30d";
    }

    private static string NormalizeTab(string? tab)
    {
        var normalized = (tab ?? "overview").Trim().ToLowerInvariant();
        return normalized is "overview" or "realtime" or "acquisition" or "engagement" or "audience" or "conversions" or "seo" or "pages"
            ? normalized
            : "overview";
    }

    private static bool NormalizeCompare(string? compare)
        => string.Equals((compare ?? "0").Trim(), "1", StringComparison.OrdinalIgnoreCase) ||
           string.Equals((compare ?? "0").Trim(), "true", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSegment(string? segment)
    {
        var normalized = (segment ?? "all").Trim().ToLowerInvariant();
        return normalized is "all" or "paid" or "organic" or "direct" or "referral" ? normalized : "all";
    }

    private static double PercentChange(double current, double previous)
    {
        if (Math.Abs(previous) < 0.00001)
        {
            if (Math.Abs(current) < 0.00001)
            {
                return 0;
            }

            return 100;
        }

        return ((current - previous) / previous) * 100.0;
    }

    private static string CountryLabel(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || string.Equals(code, "UNK", StringComparison.OrdinalIgnoreCase))
        {
            return "Unknown";
        }

        try
        {
            var region = new RegionInfo(code);
            return region.EnglishName;
        }
        catch
        {
            return code.ToUpperInvariant();
        }
    }

    private static string DeviceLabel(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "desktop" => "Desktop",
            "mobile" => "Mobile",
            "tablet" => "Tablet",
            _ => "Other"
        };
    }

    private static DeviceBreakdownView BuildDeviceBreakdown(AnalyticsDeviceSummary summary)
    {
        var total = Math.Max(1, summary.Desktop + summary.Mobile + summary.Tablet + summary.Other);
        return new DeviceBreakdownView(
            summary.Desktop,
            summary.Mobile,
            summary.Tablet,
            summary.Other,
            Math.Round(summary.Desktop * 100.0 / total, 1),
            Math.Round(summary.Mobile * 100.0 / total, 1),
            Math.Round(summary.Tablet * 100.0 / total, 1),
            Math.Round(summary.Other * 100.0 / total, 1));
    }

    private static TrackingReadinessView BuildReadiness(string selectedDomain, AnalyticsSnapshot snapshot)
    {
        var hasTraffic = snapshot.Current.Pageviews > 0 || snapshot.Current.Sessions > 0;
        var hasConversions = snapshot.ConversionCount > 0;
        var trackerLikelyActive = hasTraffic || hasConversions;
        var message = !hasTraffic
            ? "No traffic detected in this range. Verify DNS, republish the project, and load public pages."
            : !hasConversions
                ? "Traffic detected, no conversions yet. Submit the contact form to verify event tracking."
                : "Tracking healthy. Traffic and conversions are being recorded.";

        return new TrackingReadinessView(
            DomainConnected: !string.IsNullOrWhiteSpace(selectedDomain),
            SslActive: !string.IsNullOrWhiteSpace(selectedDomain),
            HasTraffic: hasTraffic,
            HasConversions: hasConversions,
            TrackerLikelyActive: trackerLikelyActive,
            LastEventSeenUtc: snapshot.LastEventSeenUtc,
            StatusText: message);
    }
}

