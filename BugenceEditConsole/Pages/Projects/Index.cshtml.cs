using BugenceEditConsole.Data;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BugenceEditConsole.Pages.Projects;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly DomainRoutingOptions _domainOptions;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IProjectSnapshotService _snapshotService;
    private readonly IProjectPublishService _projectPublishService;

    public IndexModel(
        ApplicationDbContext db,
        IOptions<DomainRoutingOptions> domainOptions,
        UserManager<ApplicationUser> userManager,
        IProjectSnapshotService snapshotService,
        IProjectPublishService projectPublishService)
    {
        _db = db;
        _domainOptions = domainOptions.Value;
        _userManager = userManager;
        _snapshotService = snapshotService;
        _projectPublishService = projectPublishService;
    }

    public IReadOnlyList<ProjectCard> Cards { get; private set; } = Array.Empty<ProjectCard>();

    public class ProjectCard
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Status { get; set; } = "Production";
        public string Branch { get; set; } = "master";
        public string RelativeTime { get; set; } = string.Empty;
        public string SizeText { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Tech { get; set; } = "HTML";
        public string Icon { get; set; } = "<i class=\"fa-solid fa-layer-group\"></i>";
        public string GradientStart { get; set; } = "#0ea5e9";
        public string GradientEnd { get; set; } = "#0ea5e9";
        public string Link { get; set; } = "#";
        public string SortDate { get; set; } = string.Empty;
        public List<string> Stack { get; set; } = new();
    }

    public async Task OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        var projectsQuery = _db.UploadedProjects.AsQueryable();
        if (user?.CompanyId != null)
        {
            projectsQuery = projectsQuery.Where(p => p.CompanyId == user.CompanyId);
        }
        else if (!string.IsNullOrWhiteSpace(user?.Id))
        {
            projectsQuery = projectsQuery.Where(p => p.UserId == user.Id);
        }
        else
        {
            projectsQuery = projectsQuery.Where(_ => false);
        }

        var projects = await projectsQuery
            .OrderByDescending(x => x.UploadedAtUtc)
            .ToListAsync();

        var projIds = projects.Select(p => p.Id).ToList();
        var files = await _db.UploadedProjectFiles
            .Where(f => projIds.Contains(f.UploadedProjectId))
            .ToListAsync();

        var palette = new (string start, string end, string icon)[]
        {
            ("#06b6d4", "#0284c7", "<i class=\"fa-brands fa-react\"></i>"),
            ("#f97316", "#b45309", "<i class=\"fa-brands fa-html5\"></i>"),
            ("#22c55e", "#0ea5e9", "<i class=\"fa-brands fa-vuejs\"></i>"),
            ("#4b5563", "#1f2937", "<i class=\"fa-brands fa-node-js\"></i>")
        };

        var now = DateTime.UtcNow;
        var cards = new List<ProjectCard>();
        for (int i = 0; i < projects.Count; i++)
        {
            var p = projects[i];
            var slug = string.IsNullOrWhiteSpace(p.Slug) ? SlugGenerator.Slugify(p.FolderName) : p.Slug;
            var projFiles = files.Where(f => f.UploadedProjectId == p.Id && !f.IsFolder).ToList();
            var firstHtml = projFiles
                .Where(f => f.RelativePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || f.RelativePath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.RelativePath.ToLowerInvariant().Contains("index") ? 0 : 1)
                .ThenBy(f => f.RelativePath.Length)
                .FirstOrDefault();
            string previewUrl = "#";
            if (firstHtml != null)
            {
                var rel = firstHtml.RelativePath.Replace("\\", "/");
                var prefix = p.FolderName.Replace("\\", "/").Trim('/') + "/";
                if (rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    rel = rel[prefix.Length..];
                previewUrl = BuildPreviewUrl(p.FolderName, rel);
            }

            var palettePick = palette[i % palette.Length];
            cards.Add(new ProjectCard
            {
                Id = p.Id,
                Name = p.FolderName,
                Domain = BuildPrimaryDomain(slug),
                Status = string.IsNullOrWhiteSpace(p.Status) ? "Production" : p.Status,
                Branch = string.IsNullOrWhiteSpace(p.Status) ? "master" : p.Status.Equals("draft", StringComparison.OrdinalIgnoreCase) ? "dev" : "main",
                RelativeTime = FormatRelative(p.UploadedAtUtc, now),
                SizeText = FormatSize(p.SizeBytes),
                SizeBytes = p.SizeBytes,
                Tech = "HTML",
                Icon = palettePick.icon,
                GradientStart = palettePick.start,
                GradientEnd = palettePick.end,
                Link = previewUrl,
                SortDate = p.UploadedAtUtc.ToString("yyyy-MM-ddTHH:mm:ss"),
                Stack = ParseStack(p.Data)
            });
        }

        Cards = cards;
    }

    private static string FormatSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }

    private static string FormatRelative(DateTime utc, DateTime now)
    {
        var diff = now - utc;
        if (diff.TotalMinutes < 1) return "Just now";
        if (diff.TotalMinutes < 60) return $"{Math.Floor(diff.TotalMinutes)}m ago";
        if (diff.TotalHours < 24) return $"{Math.Floor(diff.TotalHours)}h ago";
        if (diff.TotalDays < 30) return $"{Math.Floor(diff.TotalDays)}d ago";
        return utc.ToLocalTime().ToString("MMM d");
    }

    private static List<string> ParseStack(byte[] data)
    {
        if (data == null || data.Length == 0) return new List<string>();
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(data);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("stack", out var stackElem) && stackElem.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                return stackElem.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            }
        }
        catch { }
        return new List<string>();
    }

    private static string BuildPreviewUrl(string folderName, string relativePath)
    {
        static string EncodePath(string input) =>
            string.Join("/", input
                .Replace("\\", "/")
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));

        var folder = EncodePath(folderName);
        var rel = EncodePath(relativePath);
        return $"/Uploads/{folder}/{rel}";
    }

    private string BuildPrimaryDomain(string slug)
    {
        var zone = _domainOptions.PrimaryZone?.Trim('.');
        if (string.IsNullOrWhiteSpace(zone))
        {
            zone = "bugence.app";
        }
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "project";
        }
        return $"{slug}.{zone}".Trim('.');
    }

    public async Task<IActionResult> OnPostPromoteAsync(int projectId, string fromEnv, string toEnv, long? snapshotId = null)
    {
        var user = await _userManager.GetUserAsync(User);
        var projectsQuery = _db.UploadedProjects.AsQueryable();
        if (user?.CompanyId != null)
        {
            projectsQuery = projectsQuery.Where(p => p.CompanyId == user.CompanyId);
        }
        else if (!string.IsNullOrWhiteSpace(user?.Id))
        {
            projectsQuery = projectsQuery.Where(p => p.UserId == user.Id);
        }
        else
        {
            projectsQuery = projectsQuery.Where(_ => false);
        }

        var project = await projectsQuery.FirstOrDefaultAsync(p => p.Id == projectId);
        if (project == null)
        {
            return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
        }

        var pointer = await _db.ProjectEnvironmentPointers.FirstOrDefaultAsync(p => p.UploadedProjectId == projectId);
        var normalizedFrom = (fromEnv ?? "draft").Trim().ToLowerInvariant();
        var normalizedTo = (toEnv ?? "staging").Trim().ToLowerInvariant();
        long? fromSnapshotId = snapshotId;
        if (!fromSnapshotId.HasValue && pointer != null)
        {
            fromSnapshotId = normalizedFrom switch
            {
                "live" => pointer.LiveSnapshotId,
                "staging" => pointer.StagingSnapshotId,
                _ => pointer.DraftSnapshotId
            };
        }

        if (!fromSnapshotId.HasValue)
        {
            return new JsonResult(new { success = false, message = "No source snapshot found." }) { StatusCode = 400 };
        }

        var sourceSnapshot = await _db.ProjectDeploySnapshots.FirstOrDefaultAsync(s => s.Id == fromSnapshotId.Value && s.UploadedProjectId == projectId);
        if (sourceSnapshot == null)
        {
            return new JsonResult(new { success = false, message = "Source snapshot missing." }) { StatusCode = 404 };
        }

        var restored = await _snapshotService.RestoreSnapshotAsync(project, sourceSnapshot, HttpContext.RequestAborted);
        if (!restored.Success)
        {
            return new JsonResult(new { success = false, message = restored.Message }) { StatusCode = 400 };
        }

        var publishOk = true;
        string publishMessage = string.Empty;
        if (normalizedTo == "live")
        {
            var published = await _projectPublishService.PublishAsync(projectId, "promotion", HttpContext.RequestAborted);
            publishOk = published.Success;
            publishMessage = published.Message;
        }

        var promotedSnapshot = await _snapshotService.CreateSnapshotAsync(
            project,
            normalizedTo,
            "promotion",
            user?.Id,
            isSuccessful: publishOk,
            versionLabel: $"promote-{normalizedFrom}-to-{normalizedTo}",
            cancellationToken: HttpContext.RequestAborted);

        return new JsonResult(new
        {
            success = publishOk,
            message = publishOk ? "Promotion complete." : publishMessage,
            sourceSnapshotId = sourceSnapshot.Id,
            promotedSnapshotId = promotedSnapshot.Snapshot?.Id,
            from = normalizedFrom,
            to = normalizedTo
        });
    }
}
