using System.Globalization;
using System.Text;
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

    public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IAnalyticsQueryService analytics)
    {
        _db = db;
        _userManager = userManager;
        _analytics = analytics;
    }

    public sealed record AnalyticsProjectOption(int Id, string Name, string DomainName);

    public IReadOnlyList<AnalyticsProjectOption> Projects { get; private set; } = Array.Empty<AnalyticsProjectOption>();
    public bool HasEligibleProjects { get; private set; }
    public int SelectedProjectId { get; private set; }
    public string SelectedProjectName { get; private set; } = "Project";
    public string SelectedDomainName { get; private set; } = string.Empty;
    public string SelectedRange { get; private set; } = "30d";

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

    public async Task<IActionResult> OnGetAsync(int? projectId, string? range)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Page();
        }

        var accessScope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        SelectedRange = NormalizeRange(range);

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

    public async Task<IActionResult> OnGetLiveAsync(int projectId, string? range)
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

        return new JsonResult(new
        {
            success = true,
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
            lastEventSeenUtc = snapshot.LastEventSeenUtc
        });
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

