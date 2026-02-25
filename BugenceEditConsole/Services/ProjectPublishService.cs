using System.Text.Json;
using System.Text;
using BugenceEditConsole.Data;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BugenceEditConsole.Services;

public sealed class ProjectPublishResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime? PublishedAtUtc { get; init; }
    public string? PublishStoragePath { get; init; }
}

public interface IProjectPublishService
{
    Task<ProjectPublishResult> PublishAsync(int projectId, string source = "unknown", CancellationToken cancellationToken = default);
}

public sealed class ProjectPublishService : IProjectPublishService
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly DomainRoutingOptions _domainOptions;

    public ProjectPublishService(
        ApplicationDbContext db,
        IWebHostEnvironment environment,
        IOptions<DomainRoutingOptions> domainOptions)
    {
        _db = db;
        _environment = environment;
        _domainOptions = domainOptions.Value;
    }

    public async Task<ProjectPublishResult> PublishAsync(int projectId, string source = "unknown", CancellationToken cancellationToken = default)
    {
        var project = await _db.UploadedProjects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return new ProjectPublishResult { Success = false, Message = "Project not found." };
        }

        var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var sourceRoot = Path.Combine(webRoot, "Uploads", project.FolderName);
        if (!Directory.Exists(sourceRoot))
        {
            return new ProjectPublishResult { Success = false, Message = "Project source folder not found." };
        }

        var publishRoot = string.IsNullOrWhiteSpace(_domainOptions.PublishRoot)
            ? "Published"
            : _domainOptions.PublishRoot.Trim('\\', '/', ' ');

        var slugTarget = Path.Combine(webRoot, publishRoot, "slugs", project.Slug);
        var projectTarget = Path.Combine(webRoot, publishRoot, "projects", project.Id.ToString());

        PublishDirectory(sourceRoot, slugTarget);
        PublishDirectory(sourceRoot, projectTarget);
        InjectAnalyticsTracker(slugTarget, project.Id);
        InjectAnalyticsTracker(projectTarget, project.Id);

        project.PublishStoragePath = Path.Combine(publishRoot, "slugs", project.Slug);
        project.LastPublishedAtUtc = DateTime.UtcNow;
        project.Status = "Published";

        var fileCount = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).Count();
        _db.PreviousDeploys.Add(new PreviousDeploy
        {
            UploadedProjectId = project.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                eventType = "publish",
                source,
                artifact = new { fileCount, path = project.PublishStoragePath }
            }),
            StoredAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        return new ProjectPublishResult
        {
            Success = true,
            Message = "Published successfully.",
            PublishedAtUtc = project.LastPublishedAtUtc,
            PublishStoragePath = project.PublishStoragePath
        };
    }

    private static void PublishDirectory(string sourceRoot, string destinationRoot)
    {
        if (Directory.Exists(destinationRoot))
        {
            Directory.Delete(destinationRoot, recursive: true);
        }

        Directory.CreateDirectory(destinationRoot);
        CopyDirectory(sourceRoot, destinationRoot);
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDir))
        {
            var name = Path.GetFileName(directory);
            if (string.Equals(name, ".bugence", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var nextDest = Path.Combine(destDir, name);
            Directory.CreateDirectory(nextDest);
            CopyDirectory(directory, nextDest);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            var dest = Path.Combine(destDir, name);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static void InjectAnalyticsTracker(string root, int projectId)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.html", SearchOption.AllDirectories))
        {
            string html;
            try
            {
                html = File.ReadAllText(file);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(html) ||
                html.Contains("id=\"bugence-analytics-tracker\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var script = BuildAnalyticsTrackerScript(projectId);
            var bodyCloseIndex = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            var updated = bodyCloseIndex >= 0 ? html.Insert(bodyCloseIndex, script) : html + script;
            File.WriteAllText(file, updated, Encoding.UTF8);
        }
    }

    private static string BuildAnalyticsTrackerScript(int projectId)
    {
        return
            "<script id=\"bugence-analytics-tracker\">\n" +
            "(function(){\n" +
            "  try {\n" +
            "    var host = location.hostname || '';\n" +
            "    var path = (location.pathname || '/') + (location.search || '');\n" +
            "    var ref = document.referrer || '';\n" +
            "    var sid = localStorage.getItem('bgx_sid');\n" +
            "    var lang = (navigator.languages && navigator.languages.length ? navigator.languages[0] : navigator.language) || '';\n" +
            "    var localeParts = String(lang).replace('_', '-').split('-');\n" +
            "    var cc = (localeParts.length > 1 && localeParts[1]) ? localeParts[1].toUpperCase() : '';\n" +
            "    var ua = navigator.userAgent || '';\n" +
            "    var dt = /ipad|tablet|playbook|silk|kindle/i.test(ua) ? 'tablet' : (/mobi|android|iphone|ipod|windows phone/i.test(ua) ? 'mobile' : 'desktop');\n" +
            "    if (!sid) {\n" +
            "      sid = (window.crypto && crypto.randomUUID) ? crypto.randomUUID().replace(/-/g, '') : (Math.random().toString(36).slice(2) + Date.now().toString(36));\n" +
            "      localStorage.setItem('bgx_sid', sid);\n" +
            "    }\n" +
            "    var src = 'https://bugence.com/api/analytics/collect.gif?pid=" + projectId + "'\n" +
            "      + '&h=' + encodeURIComponent(host)\n" +
            "      + '&p=' + encodeURIComponent(path)\n" +
            "      + '&r=' + encodeURIComponent(ref)\n" +
            "      + '&sid=' + encodeURIComponent(sid)\n" +
            "      + '&cc=' + encodeURIComponent(cc)\n" +
            "      + '&dt=' + encodeURIComponent(dt)\n" +
            "      + '&t=' + Date.now();\n" +
            "    var img = new Image();\n" +
            "    img.referrerPolicy = 'no-referrer-when-downgrade';\n" +
            "    img.src = src;\n" +
            "    var dedup = {};\n" +
            "    var isEligibleForm = function(form){\n" +
            "      if (!form) return false;\n" +
            "      var action = String(form.getAttribute('action') || '').toLowerCase();\n" +
            "      var pathLower = String(location.pathname || '').toLowerCase();\n" +
            "      var names = Array.prototype.slice.call(form.querySelectorAll('input,textarea,select'))\n" +
            "        .map(function(x){ return String(x.name || x.id || '').toLowerCase(); }).join(' ');\n" +
            "      return action.indexOf('contact') >= 0 || action.indexOf('lead') >= 0 || action.indexOf('inquiry') >= 0 || action.indexOf('enquiry') >= 0 || action.indexOf('quote') >= 0\n" +
            "        || pathLower.indexOf('contact') >= 0 || names.indexOf('email') >= 0 || names.indexOf('phone') >= 0 || names.indexOf('message') >= 0 || names.indexOf('name') >= 0;\n" +
            "    };\n" +
            "    var sendEvent = function(eventName){\n" +
            "      var key = eventName + '|' + path;\n" +
            "      var nowTs = Date.now();\n" +
            "      if (dedup[key] && (nowTs - dedup[key]) < 30000) return;\n" +
            "      dedup[key] = nowTs;\n" +
            "      var eventSrc = 'https://bugence.com/api/analytics/event.gif?pid=" + projectId + "'\n" +
            "        + '&h=' + encodeURIComponent(host)\n" +
            "        + '&p=' + encodeURIComponent(path)\n" +
            "        + '&r=' + encodeURIComponent(ref)\n" +
            "        + '&sid=' + encodeURIComponent(sid)\n" +
            "        + '&cc=' + encodeURIComponent(cc)\n" +
            "        + '&dt=' + encodeURIComponent(dt)\n" +
            "        + '&et=' + encodeURIComponent('form_submit')\n" +
            "        + '&en=' + encodeURIComponent(eventName)\n" +
            "        + '&t=' + nowTs;\n" +
            "      var evImg = new Image();\n" +
            "      evImg.referrerPolicy = 'no-referrer-when-downgrade';\n" +
            "      evImg.src = eventSrc;\n" +
            "    };\n" +
            "    document.addEventListener('submit', function(e){\n" +
            "      var form = e && e.target;\n" +
            "      if (!isEligibleForm(form)) return;\n" +
            "      sendEvent('contact_form_submit');\n" +
            "    }, true);\n" +
            "  } catch (e) {}\n" +
            "})();\n" +
            "</script>\n";
    }
}
