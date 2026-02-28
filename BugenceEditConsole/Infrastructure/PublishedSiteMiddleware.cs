using BugenceEditConsole.Data;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.IO;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Infrastructure;

public class PublishedSiteMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DomainRoutingOptions _options;
    private readonly string _webRoot;
    private readonly FileExtensionContentTypeProvider _contentTypes = new();
    private readonly ILogger<PublishedSiteMiddleware> _logger;

    public PublishedSiteMiddleware(
        RequestDelegate next,
        IWebHostEnvironment environment,
        IOptions<DomainRoutingOptions> options,
        ILogger<PublishedSiteMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
        _webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
    }

    public async Task InvokeAsync(HttpContext context, IDomainRouter router, ApplicationDbContext db, RepeaterTemplateService repeaterService, DebugPanelLogService debugLogService, IAnalyticsIngestService analyticsIngest)
    {
        if (ShouldBypassPublishedSite(context.Request.Path))
        {
            await _next(context);
            return;
        }

        try
        {
            var host = context.Request.Host.Host;
            if (string.IsNullOrWhiteSpace(host))
            {
                await _next(context);
                return;
            }

            var route = await router.ResolveAsync(host, context.RequestAborted);
            if (route is null)
            {
                await _next(context);
                return;
            }

            if (IsAnalyticsCollectPath(context.Request.Path))
            {
                await HandleAnalyticsCollectAsync(context, db, analyticsIngest, route.ProjectId, host);
                return;
            }

            if (IsAnalyticsEventPath(context.Request.Path))
            {
                await HandleAnalyticsEventAsync(context, db, analyticsIngest, route.ProjectId, host);
                return;
            }

            var projectDetails = await db.UploadedProjects
                .AsNoTracking()
                .Where(project => project.Id == route.ProjectId)
                .Select(project => new
                {
                    project.UserId,
                    project.CompanyId,
                    project.LocalPreviewPath,
                    project.PageRouteOverridesJson
                })
                .FirstOrDefaultAsync(context.RequestAborted);
            var aliases = ProjectRoutingSettings.ParseAliases(projectDetails?.PageRouteOverridesJson);

            var publishRoot = string.IsNullOrWhiteSpace(_options.PublishRoot)
                ? "Published"
                : _options.PublishRoot.Trim('\\', '/', ' ');
            var relative = route.PublishStoragePath;
            if (string.IsNullOrWhiteSpace(relative))
            {
                relative = Path.Combine(publishRoot, "slugs", route.Slug);
            }

            var normalizedRelative = relative
                .Replace('/', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            var physicalRoot = Path.Combine(_webRoot, normalizedRelative);
            if (!Directory.Exists(physicalRoot))
            {
                _logger.LogWarning("Published path {Path} missing for host {Host}", physicalRoot, host);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("Site not published.");
                return;
            }

            if (context.Request.Path.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase))
            {
                await WriteRobotsAsync(context, host);
                return;
            }

            if (context.Request.Path.Equals("/sitemap.xml", StringComparison.OrdinalIgnoreCase))
            {
                await WriteSitemapAsync(context, host, physicalRoot, projectDetails?.LocalPreviewPath, aliases);
                return;
            }
            var resolvedRoutePath = ProjectRoutingSettings.ResolveIncomingPath(
                context.Request.Path.HasValue ? context.Request.Path.Value : string.Empty,
                projectDetails?.LocalPreviewPath,
                aliases);

            var requestPath = string.IsNullOrWhiteSpace(resolvedRoutePath)
                ? (context.Request.Path.HasValue && context.Request.Path != "/"
                    ? context.Request.Path.Value!
                    : "/index.html")
                : "/" + resolvedRoutePath.TrimStart('/');
            var cleanPath = requestPath.TrimStart('/');
            var physicalFile = Path.Combine(physicalRoot, cleanPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(physicalFile))
            {
                var requestedExt = Path.GetExtension(requestPath);
                if (!string.IsNullOrWhiteSpace(requestedExt))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                physicalFile = Path.Combine(physicalRoot, "index.html");
                if (!File.Exists(physicalFile))
                {
                    await _next(context);
                    return;
                }
            }

            if (!_contentTypes.TryGetContentType(physicalFile, out var contentType))
            {
                contentType = "application/octet-stream";
            }

            if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                var html = await File.ReadAllTextAsync(physicalFile, context.RequestAborted);
                var projectOwner = projectDetails;

                if (html.Contains("<Repeater-", StringComparison.OrdinalIgnoreCase) ||
                    html.Contains("<SubTemplete-", StringComparison.OrdinalIgnoreCase) ||
                    html.Contains("<Workflow-", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(projectOwner?.UserId))
                    {
                        try
                        {
                            html = await repeaterService.RenderAsync(html, projectOwner.UserId, context.RequestAborted);
                        }
                        catch (Exception ex)
                        {
                            await debugLogService.LogErrorAsync(
                                source: "PublishedSiteMiddleware.RepeaterRender",
                                shortDescription: ex.Message,
                                longDescription: ex.ToString(),
                                ownerUserId: projectOwner.UserId,
                                path: context.Request.Path,
                                cancellationToken: context.RequestAborted);
                        }
                    }
                }
                html = EnsureWorkflowRunnerScript(html);
                html = EnsureAnalyticsTrackerScript(html);
                context.Response.ContentType = "text/html";
                context.Response.Headers["Cache-Control"] = "public,max-age=60";
                await context.Response.WriteAsync(html, context.RequestAborted);
                return;
            }

            context.Response.ContentType = contentType;
            context.Response.Headers["Cache-Control"] = "public,max-age=60";
            await context.Response.SendFileAsync(physicalFile, context.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Published site middleware failed for {Host}{Path}. Falling back to platform pipeline.", context.Request.Host.Host, context.Request.Path);
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            await _next(context);
        }
    }

    private static string EnsureWorkflowRunnerScript(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html;
        }

        const string scriptTag = "<script src=\"/js/workflow-trigger-runner.js\"></script>";
        if (html.Contains("/js/workflow-trigger-runner.js", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        var bodyCloseIndex = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyCloseIndex >= 0)
        {
            return html.Insert(bodyCloseIndex, scriptTag);
        }

        return html + scriptTag;
    }

    private static async Task WriteRobotsAsync(HttpContext context, string host)
    {
        var robots = $"User-agent: *\nAllow: /\nSitemap: https://{host}/sitemap.xml\n";
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "public,max-age=300";
        await context.Response.WriteAsync(robots, context.RequestAborted);
    }

    private static async Task WriteSitemapAsync(
        HttpContext context,
        string host,
        string physicalRoot,
        string? landingPagePath,
        IReadOnlyDictionary<string, string> aliases)
    {
        var pages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "/" };
        foreach (var file in Directory.EnumerateFiles(physicalRoot, "*.html", SearchOption.AllDirectories))
        {
            var relative = ProjectRoutingSettings.NormalizeFilePath(Path.GetRelativePath(physicalRoot, file)
                .Replace(Path.DirectorySeparatorChar, '/')
                .TrimStart('/'));

            if (string.Equals(relative, "index.html", StringComparison.OrdinalIgnoreCase))
            {
                pages.Add("/");
                continue;
            }

            if (aliases.TryGetValue(relative, out var aliasRoute) && !string.IsNullOrWhiteSpace(aliasRoute))
            {
                pages.Add("/" + aliasRoute.Trim('/'));
            }
            else
            {
                var route = ProjectRoutingSettings.NormalizeRoutePath(relative);
                if (!string.IsNullOrWhiteSpace(route))
                {
                    pages.Add("/" + route);
                }
            }
        }

        var landingRoute = ProjectRoutingSettings.ResolveIncomingPath("/", landingPagePath, aliases);
        if (!string.IsNullOrWhiteSpace(landingRoute) && !string.Equals(landingRoute, "index.html", StringComparison.OrdinalIgnoreCase))
        {
            var landingAlias = aliases.TryGetValue(landingRoute, out var aliasRoute)
                ? aliasRoute
                : ProjectRoutingSettings.NormalizeRoutePath(landingRoute);
            if (!string.IsNullOrWhiteSpace(landingAlias))
            {
                pages.Add("/" + landingAlias.Trim('/'));
            }
        }

        var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var page in pages.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("<url><loc>");
            sb.Append(System.Security.SecurityElement.Escape($"https://{host}{page}"));
            sb.Append("</loc><lastmod>");
            sb.Append(now);
            sb.Append("</lastmod></url>");
        }
        sb.Append("</urlset>");

        context.Response.ContentType = "application/xml; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "public,max-age=300";
        await context.Response.WriteAsync(sb.ToString(), context.RequestAborted);
    }

    private static string EnsureAnalyticsTrackerScript(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html;
        }

        if (html.Contains("id=\"bugence-analytics-tracker\"", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        const string scriptTag = """
<script id="bugence-analytics-tracker">
(function(){
  try {
    var endpoint = "/__bgx/collect";
    var lang = (navigator.languages && navigator.languages.length ? navigator.languages[0] : navigator.language) || "";
    var localeParts = String(lang).replace('_', '-').split('-');
    var countryCode = (localeParts.length > 1 && localeParts[1]) ? localeParts[1].toUpperCase() : "";
    var ua = navigator.userAgent || "";
    var deviceType = /ipad|tablet|playbook|silk|kindle/i.test(ua) ? "tablet"
      : (/mobi|android|iphone|ipod|windows phone/i.test(ua) ? "mobile" : "desktop");
    var payload = {
      path: location.pathname + location.search,
      referrer: document.referrer || "",
      countryCode: countryCode,
      deviceType: deviceType
    };
    var body = JSON.stringify(payload);
    if (navigator.sendBeacon) {
      var blob = new Blob([body], { type: "application/json" });
      navigator.sendBeacon(endpoint, blob);
      return;
    }
    fetch(endpoint, {
      method: "POST",
      credentials: "include",
      keepalive: true,
      headers: { "Content-Type": "application/json" },
      body: body
    }).catch(function(){});
  } catch (e) {}
})();

(function(){
  try {
    var EVENT_ENDPOINT = "/__bgx/event";
    var dedup = {};
    var now = function(){ return Date.now(); };
    var normalize = function(v){ return String(v || "").toLowerCase(); };
    var isEligibleForm = function(form){
      if (!form) return false;
      var action = normalize(form.getAttribute("action"));
      var path = normalize(location.pathname);
      var names = Array.prototype.slice.call(form.querySelectorAll("input,textarea,select"))
        .map(function(x){ return normalize(x.name || x.id); })
        .join(" ");
      return action.includes("contact") || action.includes("lead") || action.includes("inquiry") || action.includes("enquiry") || action.includes("quote")
        || path.includes("contact")
        || names.includes("email") || names.includes("phone") || names.includes("message") || names.includes("name");
    };
    var sendEvent = function(name){
      var key = name + "|" + location.pathname;
      if (dedup[key] && now() - dedup[key] < 30000) return;
      dedup[key] = now();
      var lang = (navigator.languages && navigator.languages.length ? navigator.languages[0] : navigator.language) || "";
      var localeParts = String(lang).replace('_', '-').split('-');
      var cc = (localeParts.length > 1 && localeParts[1]) ? localeParts[1].toUpperCase() : "";
      var ua = navigator.userAgent || "";
      var dt = /ipad|tablet|playbook|silk|kindle/i.test(ua) ? "tablet"
        : (/mobi|android|iphone|ipod|windows phone/i.test(ua) ? "mobile" : "desktop");
      var body = JSON.stringify({
        eventType: "form_submit",
        eventName: name,
        path: location.pathname + location.search,
        referrer: document.referrer || "",
        countryCode: cc,
        deviceType: dt
      });
      if (navigator.sendBeacon) {
        var blob = new Blob([body], { type: "application/json" });
        navigator.sendBeacon(EVENT_ENDPOINT, blob);
        return;
      }
      fetch(EVENT_ENDPOINT, {
        method: "POST",
        credentials: "include",
        keepalive: true,
        headers: { "Content-Type": "application/json" },
        body: body
      }).catch(function(){});
    };

    document.addEventListener("submit", function(e){
      var form = e && e.target;
      if (!isEligibleForm(form)) return;
      sendEvent("contact_form_submit");
    }, true);
  } catch (e) {}
})();
</script>
""";

        var bodyCloseIndex = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyCloseIndex >= 0)
        {
            return html.Insert(bodyCloseIndex, scriptTag);
        }

        return html + scriptTag;
    }

    private static bool IsAnalyticsCollectPath(PathString path)
    {
        return path.Equals("/__bgx/collect", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldBypassPublishedSite(PathString path)
    {
        if (!path.HasValue)
        {
            return false;
        }

        if (path.Equals("/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Always let platform application routes execute through Razor/API pipeline.
        // This prevents published-site resolution from intercepting login/admin paths.
        if (path.StartsWithSegments("/Auth", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/app", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Error", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Dashboard", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Overview", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Projects", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Analytics", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Tools", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Support", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Workflow", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/WorkFlow", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/DynamicVE", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Application", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Editor", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/ProjectHub", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Insights", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Permissions", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Workflows", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/js", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/css", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/lib", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAnalyticsEventPath(PathString path)
    {
        return path.Equals("/__bgx/event", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task HandleAnalyticsCollectAsync(
        HttpContext context,
        ApplicationDbContext db,
        IAnalyticsIngestService analyticsIngest,
        int projectId,
        string host)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        string? ownerUserId = null;
        Guid? companyId = null;
        var projectOwner = await db.UploadedProjects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.UserId, p.CompanyId })
            .FirstOrDefaultAsync(context.RequestAborted);
        if (projectOwner is not null)
        {
            ownerUserId = projectOwner.UserId;
            companyId = projectOwner.CompanyId;
        }

        string? requestedPath = null;
        string? requestedReferrer = null;
        string? requestedCountryCode = null;
        string? requestedDeviceType = null;
        try
        {
            var payload = await JsonSerializer.DeserializeAsync<AnalyticsCollectPayload>(
                context.Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                context.RequestAborted);

            requestedPath = payload?.Path;
            requestedReferrer = payload?.Referrer;
            requestedCountryCode = payload?.CountryCode;
            requestedDeviceType = payload?.DeviceType;
        }
        catch
        {
            // Keep best-effort behavior for malformed payloads.
        }

        await TrackAnalyticsAsync(
            context,
            analyticsIngest,
            projectId,
            host,
            ownerUserId,
            companyId,
            requestedPath,
            requestedReferrer,
            requestedCountryCode,
            requestedDeviceType);

        context.Response.StatusCode = StatusCodes.Status204NoContent;
        context.Response.Headers["Cache-Control"] = "no-store";
    }

    private static async Task HandleAnalyticsEventAsync(
        HttpContext context,
        ApplicationDbContext db,
        IAnalyticsIngestService analyticsIngest,
        int projectId,
        string host)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        var projectOwner = await db.UploadedProjects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.UserId, p.CompanyId })
            .FirstOrDefaultAsync(context.RequestAborted);

        string? eventType = null;
        string? eventName = null;
        string? path = null;
        string? referrer = null;
        string? countryCode = null;
        string? deviceType = null;
        try
        {
            var payload = await JsonSerializer.DeserializeAsync<AnalyticsEventPayload>(
                context.Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                context.RequestAborted);

            eventType = payload?.EventType;
            eventName = payload?.EventName;
            path = payload?.Path;
            referrer = payload?.Referrer;
            countryCode = payload?.CountryCode;
            deviceType = payload?.DeviceType;
        }
        catch
        {
        }

        var sessionId = context.Request.Cookies.TryGetValue("_bgx_sid", out var existingSid) && !string.IsNullOrWhiteSpace(existingSid)
            ? existingSid!
            : Guid.NewGuid().ToString("N");

        context.Response.Cookies.Append("_bgx_sid", sessionId, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            MaxAge = TimeSpan.FromDays(30)
        });

        string? referrerHost = null;
        if (Uri.TryCreate(referrer, UriKind.Absolute, out var refUri))
        {
            referrerHost = refUri.Host;
        }

        await analyticsIngest.TrackEventAsync(new AnalyticsEventIngestContext(
            ProjectId: projectId,
            SessionId: sessionId,
            EventType: string.IsNullOrWhiteSpace(eventType) ? "form_submit" : eventType,
            EventName: string.IsNullOrWhiteSpace(eventName) ? "contact_form_submit" : eventName,
            Path: string.IsNullOrWhiteSpace(path) ? "/" : path,
            PageTitle: null,
            CountryCode: countryCode ?? string.Empty,
            DeviceType: deviceType,
            Language: null,
            ReferrerHost: referrerHost,
            MetadataJson: null,
            UtmSource: null,
            UtmMedium: null,
            UtmCampaign: null,
            UtmTerm: null,
            UtmContent: null,
            OccurredAtUtc: DateTime.UtcNow,
            OwnerUserId: projectOwner?.UserId,
            CompanyId: projectOwner?.CompanyId), context.RequestAborted);

        context.Response.StatusCode = StatusCodes.Status204NoContent;
        context.Response.Headers["Cache-Control"] = "no-store";
    }

    private static async Task TrackAnalyticsAsync(
        HttpContext context,
        IAnalyticsIngestService analyticsIngest,
        int projectId,
        string host,
        string? ownerUserId,
        Guid? companyId,
        string? pathOverride = null,
        string? referrerOverride = null,
        string? countryOverride = null,
        string? deviceTypeOverride = null)
    {
        try
        {
            var path = string.IsNullOrWhiteSpace(pathOverride)
                ? (context.Request.Path.Value ?? "/")
                : pathOverride;
            if (ShouldSkipAnalyticsPath(path))
            {
                return;
            }

            var userAgent = context.Request.Headers.UserAgent.ToString();
            var isBot = IsLikelyBot(userAgent);
            if (isBot)
            {
                return;
            }

            var sessionId = context.Request.Cookies.TryGetValue("_bgx_sid", out var existingSid) && !string.IsNullOrWhiteSpace(existingSid)
                ? existingSid!
                : Guid.NewGuid().ToString("N");

            context.Response.Cookies.Append("_bgx_sid", sessionId, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                MaxAge = TimeSpan.FromDays(30)
            });

            var country = string.IsNullOrWhiteSpace(countryOverride)
                ? context.Request.Headers["CF-IPCountry"].ToString()
                : countryOverride;
            if (string.IsNullOrWhiteSpace(country))
            {
                country = context.Request.Headers["X-Country"].ToString();
            }

            var referer = string.IsNullOrWhiteSpace(referrerOverride)
                ? context.Request.Headers.Referer.ToString()
                : referrerOverride;
            string? referrerHost = null;
            if (Uri.TryCreate(referer, UriKind.Absolute, out var refUri))
            {
                referrerHost = refUri.Host;
            }

            var language = context.Request.Headers.AcceptLanguage.ToString();
            if (!string.IsNullOrWhiteSpace(language))
            {
                var idx = language.IndexOf(',');
                if (idx >= 0)
                {
                    language = language[..idx];
                }
            }

            await analyticsIngest.TrackPageViewAsync(new AnalyticsIngestContext(
                ProjectId: projectId,
                Host: host,
                Path: path,
                SessionId: sessionId,
                CountryCode: country,
                DeviceType: deviceTypeOverride,
                City: null,
                Language: language,
                ReferrerHost: referrerHost,
                PageTitle: null,
                LandingPath: path,
                EngagementTimeMs: null,
                UtmSource: context.Request.Query["utm_source"].FirstOrDefault(),
                UtmMedium: context.Request.Query["utm_medium"].FirstOrDefault(),
                UtmCampaign: context.Request.Query["utm_campaign"].FirstOrDefault(),
                UtmTerm: context.Request.Query["utm_term"].FirstOrDefault(),
                UtmContent: context.Request.Query["utm_content"].FirstOrDefault(),
                UserAgent: userAgent,
                IsBot: false,
                OccurredAtUtc: DateTime.UtcNow,
                OwnerUserId: ownerUserId,
                CompanyId: companyId), context.RequestAborted);
        }
        catch
        {
            // Never break rendering because analytics fails.
        }
    }

    private static bool ShouldSkipAnalyticsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return false;
        }

        if (path.StartsWith("/__bgx/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ext = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(ext) && ext is not ".html" and not ".htm";
    }

    private static bool IsLikelyBot(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return false;
        }

        return userAgent.Contains("bot", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("crawler", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("spider", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("slurp", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class AnalyticsCollectPayload
    {
        public string? Path { get; set; }
        public string? Referrer { get; set; }
        public string? CountryCode { get; set; }
        public string? DeviceType { get; set; }
    }

    private sealed class AnalyticsEventPayload
    {
        public string? EventType { get; set; }
        public string? EventName { get; set; }
        public string? Path { get; set; }
        public string? Referrer { get; set; }
        public string? CountryCode { get; set; }
        public string? DeviceType { get; set; }
    }
}
