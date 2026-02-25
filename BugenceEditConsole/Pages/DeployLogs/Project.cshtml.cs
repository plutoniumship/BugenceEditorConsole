using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Text.Json;

namespace BugenceEditConsole.Pages.DeployLogs;

public class ProjectModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly IProjectSnapshotService _snapshotService;

    public ProjectModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment env,
        IProjectSnapshotService snapshotService)
    {
        _db = db;
        _userManager = userManager;
        _env = env;
        _snapshotService = snapshotService;
    }

    public string ProjectName { get; private set; } = "Project";
    public string LastPublish { get; private set; } = "-";
    public string LatestStatus { get; private set; } = "-";
    public int ProjectId { get; private set; }
    public bool HasRestoreBackup { get; private set; }
    public IReadOnlyList<DeployEntry> Entries { get; private set; } = Array.Empty<DeployEntry>();
    public IReadOnlyList<ProjectDeploySnapshot> Snapshots { get; private set; } = Array.Empty<ProjectDeploySnapshot>();

    public class DeployEntry
    {
        public string BuildId { get; set; } = string.Empty;
        public string Status { get; set; } = "Success";
        public string PublishedAt { get; set; } = string.Empty;
        public string FailureReason { get; set; } = "-";
        public string Description { get; set; } = string.Empty;
        public DateTime SortUtc { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int projectId)
    {
        ProjectId = projectId;
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
            return RedirectToPage("/DeployLogs/Index");
        }

        ProjectName = project.FolderName;
        HasRestoreBackup = System.IO.File.Exists(Path.Combine(_env.ContentRootPath, "App_Data", "restore", project.Id.ToString(), "restore.zip"));

        var entries = new List<DeployEntry>
        {
            new()
            {
                BuildId = $"upl_{project.Id:0000}",
                Status = NormalizeStatus(project.Status),
                PublishedAt = project.UploadedAtUtc.ToLocalTime().ToString("MMM d, h:mm tt"),
                FailureReason = "-",
                Description = "Latest uploaded project snapshot.",
                SortUtc = project.UploadedAtUtc
            }
        };

        var previous = await _db.PreviousDeploys
            .Where(p => p.UploadedProjectId == project.Id)
            .OrderByDescending(p => p.StoredAtUtc)
            .ToListAsync();

        entries.AddRange(previous.Select(MapDeployEntry));

        Entries = entries
            .OrderByDescending(e => e.SortUtc)
            .ToList();

        Snapshots = await _db.ProjectDeploySnapshots
            .Where(s => s.UploadedProjectId == project.Id)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(50)
            .ToListAsync();

        if (Snapshots.Count == 0)
        {
            await _snapshotService.CreateSnapshotAsync(
                project,
                "live",
                "backfill",
                user?.Id,
                isSuccessful: true,
                versionLabel: "initial",
                cancellationToken: HttpContext.RequestAborted);

            Snapshots = await _db.ProjectDeploySnapshots
                .Where(s => s.UploadedProjectId == project.Id)
                .OrderByDescending(s => s.CreatedAtUtc)
                .Take(50)
                .ToListAsync();
        }

        if (Entries.Count > 0)
        {
            LastPublish = Entries.First().PublishedAt;
            LatestStatus = Entries.First().Status;
        }

        return Page();
    }

    public async Task<IActionResult> OnGetDownloadLatestAsync(int projectId)
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
            return NotFound("Project not found.");
        }

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var sourcePath = ResolveLatestSourcePath(project, webRoot);
        if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
        {
            return NotFound("Latest project folder could not be found.");
        }

        await using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddDirectoryToZip(archive, sourcePath, project.FolderName);
        }

        zipStream.Position = 0;
        var fileName = $"{SanitizeFileName(project.FolderName)}-latest-{DateTime.UtcNow:yyyyMMddHHmmss}.zip";
        return File(zipStream.ToArray(), "application/zip", fileName);
    }

    private static string NormalizeStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "Success";
        }

        var normalized = status.Trim().ToLowerInvariant();
        if (normalized.Contains("fail") || normalized.Contains("error"))
        {
            return "Failed";
        }

        return "Success";
    }

    private DeployEntry MapDeployEntry(PreviousDeploy deploy)
    {
        var eventType = "deploy";
        var source = "system";
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(deploy.PayloadJson ?? "{}");
            if (doc.RootElement.TryGetProperty("eventType", out var eventTypeNode))
            {
                eventType = eventTypeNode.GetString() ?? eventType;
            }
            if (doc.RootElement.TryGetProperty("source", out var sourceNode))
            {
                source = sourceNode.GetString() ?? source;
            }
            if (doc.RootElement.TryGetProperty("message", out var messageNode))
            {
                message = messageNode.GetString();
            }
        }
        catch
        {
        }

        var description = message;
        if (string.IsNullOrWhiteSpace(description))
        {
            description = eventType switch
            {
                "reupload" => $"Project folder re-uploaded ({source}).",
                "restore" => $"Previous backup restored ({source}).",
                "publish" => $"Project published ({source}).",
                _ => $"Deployment event recorded ({source})."
            };
        }

        return new DeployEntry
        {
            BuildId = $"evt_{deploy.Id:0000}",
            Status = "Success",
            PublishedAt = deploy.StoredAtUtc.ToLocalTime().ToString("MMM d, h:mm tt"),
            FailureReason = "-",
            Description = description,
            SortUtc = deploy.StoredAtUtc
        };
    }

    private static string ResolveLatestSourcePath(UploadedProject project, string webRoot)
    {
        if (!string.IsNullOrWhiteSpace(project.PublishStoragePath))
        {
            var normalized = project.PublishStoragePath
                .Replace('/', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            var publishedPath = Path.Combine(webRoot, normalized);
            if (Directory.Exists(publishedPath))
            {
                return publishedPath;
            }
        }

        var uploadsPath = Path.Combine(webRoot, "Uploads", project.FolderName);
        return Directory.Exists(uploadsPath) ? uploadsPath : string.Empty;
    }

    private static void AddDirectoryToZip(ZipArchive archive, string sourceRoot, string rootEntryName)
    {
        var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
            var entryName = $"{rootEntryName}/{relative}";
            archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }
    }

    private static string SanitizeFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "project";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(input.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "project" : safe;
    }
}
