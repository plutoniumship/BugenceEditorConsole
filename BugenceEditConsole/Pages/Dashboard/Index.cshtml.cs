using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using BugenceEditConsole.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.Text.Json;
using System.Text;
using System.IO.Compression;

namespace BugenceEditConsole.Pages.Dashboard;

[RequestFormLimits(
    MultipartBodyLengthLimit = long.MaxValue,
    ValueCountLimit = int.MaxValue,
    KeyLengthLimit = int.MaxValue,
    ValueLengthLimit = int.MaxValue,
    MultipartBoundaryLengthLimit = int.MaxValue,
    MultipartHeadersCountLimit = int.MaxValue,
    MultipartHeadersLengthLimit = int.MaxValue)]
[RequestSizeLimit(long.MaxValue)]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IContentOrchestrator _content;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly DomainRoutingOptions _domainOptions;
    private readonly IProjectDomainService _domainService;
    private readonly IProjectPublishService _projectPublishService;
    private readonly IDomainVerificationService _domainVerificationService;

    public IndexModel(
        ApplicationDbContext db,
        IContentOrchestrator content,
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment env,
        IOptions<DomainRoutingOptions> domainOptions,
        IProjectDomainService domainService,
        IProjectPublishService projectPublishService,
        IDomainVerificationService domainVerificationService)
    {
        _db = db;
        _content = content;
        _userManager = userManager;
        _env = env;
        _domainOptions = domainOptions.Value;
        _domainService = domainService;
        _projectPublishService = projectPublishService;
        _domainVerificationService = domainVerificationService;
    }

    public string Greeting { get; private set; } = "Creator";
    public string UserDisplayName { get; private set; } = "Administrator";
    public string UserEmail { get; private set; } = "admin@bugence.com";
    public string UserInitials { get; private set; } = "AD";

    public IReadOnlyList<SitePage> Pages { get; private set; } = Array.Empty<SitePage>();

    public IReadOnlyList<ContentChangeLog> RecentLogs { get; private set; } = Array.Empty<ContentChangeLog>();

    public IReadOnlyList<UploadedProject> UploadedProjects { get; private set; } = Array.Empty<UploadedProject>();
    public IReadOnlyList<ProjectSummary> ProjectSummaries { get; private set; } = Array.Empty<ProjectSummary>();
    public IReadOnlyList<PreviousDeploy> PreviousDeploys { get; private set; } = Array.Empty<PreviousDeploy>();

    public class ProjectSummary
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string SizeDisplay { get; set; } = "0 B";
        public string Uploaded { get; set; } = string.Empty;
        public string Status { get; set; } = "Uploaded";
        public string OriginalFileName { get; set; } = string.Empty;
        public string PreviewUrl { get; set; } = "#";
        public string Domain { get; set; } = string.Empty;
        public string LiveUrl { get; set; } = "#";
        public string LiveLabel { get; set; } = "Open project hub";
        public string LiveType { get; set; } = "local-preview";
        public bool DomainConnected { get; set; }
    }

    public int TotalPageCount { get; private set; }

    public int EditableTextBlocks { get; private set; }

    public int EditableImageBlocks { get; private set; }

    public DateTime? LastPublishedUtc { get; private set; }

    public string? BusinessName { get; private set; }
    public string PublishSuccessRate7d { get; private set; } = "0%";
    public int BlockedPublishes7d { get; private set; }
    public int Rollbacks30d { get; private set; }
    public string MeanRecoveryTime { get; private set; } = "-";

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user != null && await PermissionSetupOnboardingService.RequiresSetupAsync(_db, user))
        {
            return Redirect("/Tools/Applications?onboarding=permissions");
        }

        Greeting = user?.GetFriendlyName() ?? "Creator";
        UserDisplayName = user?.GetFriendlyName();
        if (string.IsNullOrWhiteSpace(UserDisplayName))
        {
            UserDisplayName = user?.UserName ?? "Administrator";
        }
        UserEmail = user?.Email ?? "admin@bugence.com";
        UserInitials = new string((UserDisplayName ?? "AD")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => char.ToUpperInvariant(p[0]))
            .Take(2)
            .ToArray());
        if (string.IsNullOrWhiteSpace(UserInitials))
        {
            UserInitials = "AD";
        }

        TotalPageCount = await _db.SitePages.CountAsync();
        EditableTextBlocks = await _db.PageSections.CountAsync(s => s.ContentType != SectionContentType.Image);
        EditableImageBlocks = await _db.PageSections.CountAsync(s => s.ContentType == SectionContentType.Image);

        LastPublishedUtc = await _db.PageSections
            .Where(s => s.LastPublishedAtUtc != null)
            .OrderByDescending(s => s.LastPublishedAtUtc)
            .Select(s => s.LastPublishedAtUtc)
            .FirstOrDefaultAsync();

        Pages = await _content.GetPagesAsync();
        RecentLogs = await _content.GetRecentLogsAsync(8);
        var scopedProjects = GetScopedProjectsQuery(user?.CompanyId, user?.Id);
        UploadedProjects = await scopedProjects
            .OrderByDescending(u => u.UploadedAtUtc)
            .Take(20)
            .ToListAsync();
        var effectiveDomains = await GetEffectiveDomainsAsync(UploadedProjects);
        var connectedCustomDomains = await GetConnectedCustomDomainsAsync(UploadedProjects);
        var fileLookup = await _db.UploadedProjectFiles
            .Where(f => UploadedProjects.Select(p => p.Id).Contains(f.UploadedProjectId))
            .ToListAsync();
        var uploadsRoot = GetUploadsRoot();
        ProjectSummaries = UploadedProjects.Select(p =>
        {
            var slug = p.Slug;
            var previewUrl = ResolvePreviewUrl(p, fileLookup);
            var sizeBytes = ResolveProjectSizeBytes(p, fileLookup, uploadsRoot);
            var hasConnectedDomain = connectedCustomDomains.TryGetValue(p.Id, out var connectedDomain);
            var liveUrl = hasConnectedDomain
                ? $"https://{connectedDomain}"
                : previewUrl != "#"
                    ? previewUrl
                    : $"/ProjectHub/Index?projectId={p.Id}";
            var liveLabel = hasConnectedDomain
                ? connectedDomain!
                : previewUrl != "#"
                    ? "Open local preview"
                    : "Open project hub";
            return new ProjectSummary
            {
                Id = p.Id,
                Name = string.IsNullOrWhiteSpace(p.DisplayName) ? p.FolderName : p.DisplayName,
                Slug = slug,
                SizeBytes = sizeBytes,
                SizeDisplay = FormatStorageSize(sizeBytes),
                Uploaded = p.UploadedAtUtc.ToLocalTime().ToString("g"),
                Status = p.Status ?? "Uploaded",
                OriginalFileName = p.OriginalFileName,
                PreviewUrl = previewUrl,
                Domain = effectiveDomains.TryGetValue(p.Id, out var domain) ? domain : BuildPrimaryDomain(slug),
                LiveUrl = liveUrl,
                LiveLabel = liveLabel,
                LiveType = hasConnectedDomain ? "custom-domain" : "local-preview",
                DomainConnected = hasConnectedDomain
            };
        }).ToList();
        PreviousDeploys = await _db.PreviousDeploys
            .Where(p => UploadedProjects.Select(u => u.Id).Contains(p.UploadedProjectId))
            .OrderByDescending(p => p.StoredAtUtc)
            .Take(10)
            .ToListAsync();

        var projectIds = UploadedProjects.Select(p => p.Id).ToList();
        var since7 = DateTime.UtcNow.AddDays(-7);
        var since30 = DateTime.UtcNow.AddDays(-30);
        var deployEvents7 = await _db.PreviousDeploys
            .Where(d => projectIds.Contains(d.UploadedProjectId) && d.StoredAtUtc >= since7)
            .ToListAsync();
        var preflight7 = await _db.ProjectPreflightRuns
            .Where(r => projectIds.Contains(r.UploadedProjectId) && r.CreatedAtUtc >= since7)
            .ToListAsync();
        BlockedPublishes7d = preflight7.Count(r => !r.Safe);

        var publishEvents = deployEvents7
            .Select(d => ParseEventType(d.PayloadJson))
            .Where(e => e == "publish" || e == "deployment" || e == "deploy")
            .ToList();
        var failedPublishes = deployEvents7.Count(d => ParseStatusFromPayload(d.PayloadJson) == "failed");
        var totalPublishes = publishEvents.Count;
        var successfulPublishes = Math.Max(0, totalPublishes - failedPublishes);
        PublishSuccessRate7d = totalPublishes <= 0
            ? "100%"
            : $"{Math.Round((double)successfulPublishes / totalPublishes * 100, 1)}%";

        var deployEvents30 = await _db.PreviousDeploys
            .Where(d => projectIds.Contains(d.UploadedProjectId) && d.StoredAtUtc >= since30)
            .OrderBy(d => d.StoredAtUtc)
            .ToListAsync();
        Rollbacks30d = deployEvents30.Count(d => ParseEventType(d.PayloadJson) == "rollback");
        MeanRecoveryTime = ComputeMeanRecovery(deployEvents30);

        var profile = user is null
            ? null
            : await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
        BusinessName = profile?.BusinessName ?? "Bugence Studio";
        return Page();
    }

    private static string ParseEventType(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return "unknown";
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("eventType", out var evt))
            {
                return (evt.GetString() ?? "unknown").Trim().ToLowerInvariant();
            }
        }
        catch
        {
        }
        return "unknown";
    }

    private static string ParseStatusFromPayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return "success";
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("status", out var status))
            {
                return (status.GetString() ?? "success").Trim().ToLowerInvariant();
            }
        }
        catch
        {
        }
        return "success";
    }

    private static string ComputeMeanRecovery(List<PreviousDeploy> events)
    {
        if (events.Count == 0) return "-";
        var rollbackPoints = events.Where(e => ParseEventType(e.PayloadJson) == "rollback").ToList();
        if (rollbackPoints.Count == 0) return "-";
        var deltas = new List<double>();
        foreach (var rollback in rollbackPoints)
        {
            var nextPublish = events.FirstOrDefault(e =>
                e.UploadedProjectId == rollback.UploadedProjectId
                && e.StoredAtUtc > rollback.StoredAtUtc
                && ParseEventType(e.PayloadJson) == "publish");
            if (nextPublish != null)
            {
                deltas.Add((nextPublish.StoredAtUtc - rollback.StoredAtUtc).TotalMinutes);
            }
        }
        if (deltas.Count == 0) return "-";
        var avg = deltas.Average();
        if (avg < 60) return $"{Math.Round(avg, 1)} min";
        return $"{Math.Round(avg / 60, 1)} hr";
    }

    public async Task<IActionResult> OnGetProjectsAsync()
    {
        var (companyId, userId) = await GetCurrentCompanyScopeAsync();
        var projects = await GetScopedProjectsQuery(companyId, userId)
            .OrderByDescending(u => u.UploadedAtUtc)
            .ToListAsync();
        var effectiveDomains = await GetEffectiveDomainsAsync(projects);
        var connectedCustomDomains = await GetConnectedCustomDomainsAsync(projects);

        var files = await _db.UploadedProjectFiles
            .Where(f => projects.Select(p => p.Id).Contains(f.UploadedProjectId))
            .ToListAsync();
        var uploadsRoot = GetUploadsRoot();

          var result = projects.Select(p =>
          {
              var slug = p.Slug;
            var previewUrl = ResolvePreviewUrl(p, files);
            var sizeBytes = ResolveProjectSizeBytes(p, files, uploadsRoot);
            var hasConnectedDomain = connectedCustomDomains.TryGetValue(p.Id, out var connectedDomain);
            var liveUrl = hasConnectedDomain
                ? $"https://{connectedDomain}"
                : previewUrl != "#"
                    ? previewUrl
                    : $"/ProjectHub/Index?projectId={p.Id}";
            var liveLabel = hasConnectedDomain
                ? connectedDomain!
                : previewUrl != "#"
                    ? "Open local preview"
                    : "Open project hub";
              return new
              {
                  id = p.Id,
                  name = string.IsNullOrWhiteSpace(p.DisplayName) ? p.FolderName : p.DisplayName,
                  folder = p.FolderName,
                  size = sizeBytes,
                  sizeDisplay = FormatStorageSize(sizeBytes),
                  uploaded = p.UploadedAtUtc.ToLocalTime().ToString("g"),
                  status = string.IsNullOrWhiteSpace(p.Status) ? "Uploaded" : p.Status,
                  original = p.OriginalFileName,
                  preview = previewUrl,
                  domain = effectiveDomains.TryGetValue(p.Id, out var domain) ? domain : BuildPrimaryDomain(slug),
                  liveUrl = liveUrl,
                  liveLabel = liveLabel,
                  liveType = hasConnectedDomain ? "custom-domain" : "local-preview",
                  domainConnected = hasConnectedDomain
              };
          }).ToList();

        return new JsonResult(result);
    }

    public async Task<IActionResult> OnGetDeploymentsAsync()
    {
        var (companyId, userId) = await GetCurrentCompanyScopeAsync();
        var items = await GetScopedProjectsQuery(companyId, userId)
            .OrderByDescending(u => u.UploadedAtUtc)
            .Select(p => new
            {
                id = p.Id,
                name = p.FolderName,
                original = p.OriginalFileName,
                uploaded = p.UploadedAtUtc.ToLocalTime().ToString("g"),
                status = p.Status
            })
            .ToListAsync();

        return new JsonResult(items);
    }

    public async Task<IActionResult> OnPostUploadAsync(List<IFormFile> upload, [FromServices] INotificationService notifications)
    {
        UploadedProject? result;
        try
        {
            var (companyId, userId) = await GetCurrentCompanyScopeAsync();
            var uploadFiles = await ResolveUploadFilesAsync(upload);
            result = await CreateProjectAsync(uploadFiles, null, null, null, userId, companyId);
            if (!string.IsNullOrWhiteSpace(userId) && result != null)
            {
                await notifications.AddAsync(userId, "Upload complete", $"\"{result.FolderName}\" is ready to preview.", "success");
            }
        }
        catch (InvalidOperationException ex)
        {
            var userId = _userManager.GetUserId(User);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                await notifications.AddAsync(userId, "Upload failed", ex.Message, "danger");
            }
            return new JsonResult(new { success = false, message = ex.Message }) { StatusCode = 400 };
        }
        catch (DbUpdateException)
        {
            var userId = _userManager.GetUserId(User);
            const string message = "Unable to save project for this account. Refresh and try again.";
            if (!string.IsNullOrWhiteSpace(userId))
            {
                await notifications.AddAsync(userId, "Upload failed", message, "danger");
            }
            return new JsonResult(new { success = false, message }) { StatusCode = 500 };
        }
        if (result is null)
        {
            return new JsonResult(new { success = false, message = "No file received." }) { StatusCode = 400 };
        }

        return new JsonResult(new
        {
            success = true,
            projectId = result.Id,
            name = result.FolderName,
            size = result.SizeBytes,
            uploadedAt = result.UploadedAtUtc
        });
    }

    public async Task<IActionResult> OnPostReuploadAsync(int projectId, List<IFormFile> upload, [FromServices] INotificationService notifications)
    {
        var (companyId, userId) = await GetCurrentCompanyScopeAsync();
        var project = await GetScopedProjectsQuery(companyId, userId)
            .Include(p => p.Files)
            .FirstOrDefaultAsync(p => p.Id == projectId);
        if (project == null)
        {
            return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
        }

        List<IFormFile> uploadFiles;
        try
        {
            uploadFiles = await ResolveUploadFilesAsync(upload);
            if (uploadFiles.Count == 0)
            {
                return new JsonResult(new { success = false, message = "No files received." }) { StatusCode = 400 };
            }
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = ex.Message }) { StatusCode = 400 };
        }

        var uploadsRoot = GetUploadsRoot();
        var projectRoot = Path.Combine(uploadsRoot, project.FolderName);
        Directory.CreateDirectory(projectRoot);

        try
        {
            await BackupProjectAsync(project.Id, projectRoot);

            var tempRoot = Path.Combine(Path.GetTempPath(), $"bugence-reupload-{project.Id}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempRoot);

            var first = uploadFiles[0];
            var relPath = NormalizePath(first.FileName);
            var isSingleArchive = uploadFiles.Count == 1 && (
                relPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                relPath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase));

            if (isSingleArchive)
            {
                await ExtractArchiveToProjectRootAsync(first, tempRoot);
            }
            else
            {
                foreach (var file in uploadFiles)
                {
                    var rel = NormalizePath(file.FileName);
                    if (string.IsNullOrWhiteSpace(rel) || rel.Contains("..", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var fullPath = Path.Combine(tempRoot, rel.Replace("/", Path.DirectorySeparatorChar.ToString()));
                    var dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
                    await file.CopyToAsync(stream);
                }
            }

            FlattenSingleTopFolder(tempRoot);

            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
            Directory.CreateDirectory(projectRoot);
            CopyDirectory(tempRoot, projectRoot);
            Directory.Delete(tempRoot, recursive: true);

            _db.UploadedProjectFiles.RemoveRange(_db.UploadedProjectFiles.Where(f => f.UploadedProjectId == project.Id));
            var files = new List<UploadedProjectFile>();
            BuildFileIndex(project.Id, projectRoot, files);
            var distinct = files
                .GroupBy(f => new { f.UploadedProjectId, f.RelativePath, f.IsFolder })
                .Select(g => g.First())
                .ToList();
            _db.UploadedProjectFiles.AddRange(distinct);

            project.SizeBytes = uploadFiles.Sum(f => f.Length);
            project.OriginalFileName = first.FileName;
            project.UploadedAtUtc = DateTime.UtcNow;
            project.Status = "Uploaded";
            _db.PreviousDeploys.Add(new PreviousDeploy
            {
                UploadedProjectId = project.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    eventType = "reupload",
                    source = "dashboard",
                    artifact = new { fileCount = distinct.Count(d => !d.IsFolder), sizeBytes = project.SizeBytes }
                }),
                StoredAtUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            var publishResult = await _projectPublishService.PublishAsync(project.Id, "dashboard-reupload", HttpContext.RequestAborted);

            var domainIds = await _db.ProjectDomains
                .Where(d => d.UploadedProjectId == project.Id && d.DomainType == ProjectDomainType.Custom)
                .Select(d => d.Id)
                .ToListAsync();
            foreach (var domainId in domainIds)
            {
                try { await _domainVerificationService.VerifyDomainAsync(domainId, HttpContext.RequestAborted); }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(userId))
            {
                await notifications.AddAsync(userId, "Re-upload complete", $"\"{project.FolderName}\" updated and published.", "success");
            }

            return new JsonResult(new
            {
                success = true,
                projectId = project.Id,
                name = project.FolderName,
                uploadedAt = project.UploadedAtUtc,
                published = publishResult.Success
            });
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(userId))
            {
                await notifications.AddAsync(userId, "Re-upload failed", ex.GetBaseException().Message, "danger");
            }
            return new JsonResult(new { success = false, message = ex.GetBaseException().Message }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostCreateProjectAsync(List<IFormFile> upload, string? name, List<string>? stack, string? description, string? repoUrl, string? owner, string? tags, string? notes, [FromServices] INotificationService notifications)
    {
        var meta = new
        {
            name,
            stack = stack ?? new List<string>(),
            description,
            repoUrl,
            owner,
            tags,
            notes
        };

        UploadedProject? result;
        try
        {
            var (companyId, userId) = await GetCurrentCompanyScopeAsync();
            var uploadFiles = await ResolveUploadFilesAsync(upload);
            result = await CreateProjectAsync(uploadFiles, name, "Draft", meta, userId, companyId);
            if (!string.IsNullOrWhiteSpace(userId) && result != null)
            {
                await notifications.AddAsync(userId, "Project created", $"\"{result.FolderName}\" is ready in Project Hub.", "success");
            }
        }
        catch (InvalidOperationException ex)
        {
            var userId = _userManager.GetUserId(User);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                await notifications.AddAsync(userId, "Project creation failed", ex.Message, "danger");
            }
            return new JsonResult(new { success = false, message = ex.Message }) { StatusCode = 400 };
        }
        catch (DbUpdateException)
        {
            var userId = _userManager.GetUserId(User);
            const string message = "Unable to create project for this account. Refresh and try again.";
            if (!string.IsNullOrWhiteSpace(userId))
            {
                await notifications.AddAsync(userId, "Project creation failed", message, "danger");
            }
            return new JsonResult(new { success = false, message }) { StatusCode = 500 };
        }
        if (result is null)
        {
            return new JsonResult(new { success = false, message = "No files attached." }) { StatusCode = 400 };
        }

        return new JsonResult(new
        {
            success = true,
            projectId = result.Id,
            name = result.FolderName,
            size = result.SizeBytes,
            uploadedAt = result.UploadedAtUtc
        });
    }

    public async Task<IActionResult> OnPostDeleteProjectAsync(int projectId)
    {
        var (companyId, userId) = await GetCurrentCompanyScopeAsync();
        var proj = await GetScopedProjectsQuery(companyId, userId)
            .Include(p => p.Files)
            .FirstOrDefaultAsync(p => p.Id == projectId);
        if (proj == null) return new JsonResult(new { success = false }) { StatusCode = 404 };

        _db.UploadedProjectFiles.RemoveRange(_db.UploadedProjectFiles.Where(f => f.UploadedProjectId == projectId));
        _db.UploadedProjects.Remove(proj);
        await _db.SaveChangesAsync();
        var uploadsRoot = GetUploadsRoot();
        var projectRoot = Path.Combine(uploadsRoot, proj.FolderName);
        if (Directory.Exists(projectRoot))
        {
            Directory.Delete(projectRoot, recursive: true);
        }
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostDeleteEntryAsync(int projectId, string path, bool isFolder)
    {
        path = NormalizePath(path);
        var (companyId, userId) = await GetCurrentCompanyScopeAsync();
        var proj = await GetScopedProjectsQuery(companyId, userId).FirstOrDefaultAsync(p => p.Id == projectId);
        if (proj == null) return new JsonResult(new { success = false }) { StatusCode = 404 };
        var files = _db.UploadedProjectFiles.Where(f => f.UploadedProjectId == projectId);
        if (isFolder)
        {
            files = files.Where(f => f.RelativePath.StartsWith(path, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            files = files.Where(f => f.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        }
        _db.UploadedProjectFiles.RemoveRange(files);
        await _db.SaveChangesAsync();
        var uploadsRoot = GetUploadsRoot();
        var projectRoot = Path.Combine(uploadsRoot, proj.FolderName);
        var fullPath = Path.Combine(projectRoot, path.Replace("/", Path.DirectorySeparatorChar.ToString()));
        if (isFolder && Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
        else if (!isFolder && System.IO.File.Exists(fullPath))
        {
            System.IO.File.Delete(fullPath);
        }
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostRenameProjectAsync(int projectId, string newName)
    {
        newName = CleanName(newName);
        if (string.IsNullOrWhiteSpace(newName))
            return new JsonResult(new { success = false, message = "Name required." }) { StatusCode = 400 };

        var (companyId, userId) = await GetCurrentCompanyScopeAsync();
        var proj = await GetScopedProjectsQuery(companyId, userId)
            .Include(p => p.Files)
            .FirstOrDefaultAsync(p => p.Id == projectId);
        if (proj == null) return new JsonResult(new { success = false }) { StatusCode = 404 };

        var oldName = proj.FolderName;
        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            return new JsonResult(new { success = true });

        var uploadsRoot = GetUploadsRoot();
        var oldRoot = Path.Combine(uploadsRoot, oldName);
        var newRoot = Path.Combine(uploadsRoot, newName);
        if (Directory.Exists(oldRoot) && !Directory.Exists(newRoot))
        {
            Directory.Move(oldRoot, newRoot);
        }

        proj.FolderName = newName;

        var prefix = oldName.TrimEnd('/') + "/";
        var newPrefix = newName.TrimEnd('/') + "/";
        foreach (var file in proj.Files)
        {
            if (file.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                file.RelativePath = newPrefix + file.RelativePath[prefix.Length..];
            }
            else if (string.Equals(file.RelativePath, oldName, StringComparison.OrdinalIgnoreCase))
            {
                file.RelativePath = newName;
            }
        }

        await _db.SaveChangesAsync();
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostRenameEntryAsync(int projectId, string path, bool isFolder, string newName)
    {
        path = NormalizePath(path);
        newName = CleanName(newName);
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(newName))
            return new JsonResult(new { success = false, message = "Invalid request." }) { StatusCode = 400 };

        var (companyId, userId) = await GetCurrentCompanyScopeAsync();
        var proj = await GetScopedProjectsQuery(companyId, userId).FirstOrDefaultAsync(p => p.Id == projectId);
        if (proj == null) return new JsonResult(new { success = false }) { StatusCode = 404 };

        var files = _db.UploadedProjectFiles.Where(f => f.UploadedProjectId == projectId);
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return new JsonResult(new { success = false, message = "Invalid path." }) { StatusCode = 400 };

        var parent = string.Join('/', segments.Take(segments.Length - 1));
        var newPath = string.IsNullOrWhiteSpace(parent) ? newName : $"{parent}/{newName}";
        var uploadsRoot = GetUploadsRoot();
        var projectRoot = Path.Combine(uploadsRoot, proj.FolderName);
        var oldFullPath = Path.Combine(projectRoot, path.Replace("/", Path.DirectorySeparatorChar.ToString()));
        var newFullPath = Path.Combine(projectRoot, newPath.Replace("/", Path.DirectorySeparatorChar.ToString()));

        if (isFolder)
        {
            if (Directory.Exists(oldFullPath) && !Directory.Exists(newFullPath))
            {
                Directory.Move(oldFullPath, newFullPath);
            }
            var prefix = path.TrimEnd('/') + "/";
            var newPrefix = newPath.TrimEnd('/') + "/";
            var toUpdate = await files
                .Where(f => f.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || f.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase))
                .ToListAsync();
            foreach (var f in toUpdate)
            {
                if (f.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    f.RelativePath = newPath;
                }
                else
                {
                    f.RelativePath = newPrefix + f.RelativePath[prefix.Length..];
                }
            }
            await _db.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        var file = await files.FirstOrDefaultAsync(f => f.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (file == null) return new JsonResult(new { success = false }) { StatusCode = 404 };
        if (System.IO.File.Exists(oldFullPath) && !System.IO.File.Exists(newFullPath))
        {
            var targetDir = Path.GetDirectoryName(newFullPath);
            if (!string.IsNullOrWhiteSpace(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
            System.IO.File.Move(oldFullPath, newFullPath);
        }
        file.RelativePath = newPath;
        await _db.SaveChangesAsync();
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostDeployLatestAsync(int projectId)
    {
        var (companyId, userId) = await GetCurrentCompanyScopeAsync();
        var hasAccess = await GetScopedProjectsQuery(companyId, userId).AnyAsync(p => p.Id == projectId);
        if (!hasAccess)
        {
            return new JsonResult(new { success = false }) { StatusCode = 404 };
        }

        var files = await _db.UploadedProjectFiles
            .Where(f => f.UploadedProjectId == projectId)
            .Select(f => new { f.RelativePath, f.SizeBytes, f.IsFolder })
            .ToListAsync();

        var snapshot = JsonSerializer.Serialize(files);
        _db.PreviousDeploys.Add(new PreviousDeploy
        {
            UploadedProjectId = projectId,
            PayloadJson = snapshot,
            StoredAtUtc = DateTime.UtcNow
        });

        _db.UploadedProjectFiles.RemoveRange(_db.UploadedProjectFiles.Where(f => f.UploadedProjectId == projectId));
        await _db.SaveChangesAsync();
        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnGetFilesAsync(int projectId, string? path)
    {
        var (companyId, userId) = await GetCurrentCompanyScopeAsync();
        var project = await GetScopedProjectsQuery(companyId, userId)
            .FirstOrDefaultAsync(p => p.Id == projectId);
        if (project is null)
        {
            return new JsonResult(new List<object>()) { StatusCode = 404 };
        }

        var basePath = NormalizePath(path ?? string.Empty);
        var uploadsRoot = GetUploadsRoot();
        var projectRoot = Path.Combine(uploadsRoot, project.FolderName);

        var files = await _db.UploadedProjectFiles
            .Where(f => f.UploadedProjectId == projectId)
            .ToListAsync();

        if (files.Count == 0 && Directory.Exists(projectRoot))
        {
            var rebuilt = new List<UploadedProjectFile>();
            BuildFileIndex(projectId, projectRoot, rebuilt);
            var distinct = rebuilt
                .GroupBy(f => new { f.UploadedProjectId, f.RelativePath, f.IsFolder })
                .Select(g => g.First())
                .ToList();
            if (distinct.Count > 0)
            {
                _db.UploadedProjectFiles.AddRange(distinct);
                project.SizeBytes = ResolveProjectSizeBytes(project, distinct, uploadsRoot);
                await _db.SaveChangesAsync();
                files = distinct;
            }
        }

        // determine immediate children of basePath
        var children = new List<object>();
        var prefix = string.IsNullOrEmpty(basePath) ? string.Empty : $"{basePath.TrimEnd('/')}/";

        var direct = files
            .Where(f => f.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                var remainder = f.RelativePath.Substring(prefix.Length);
                var parts = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries);
                return (f, parts);
            })
            .Where(x => x.parts.Length > 0)
            .ToList();

        // folders
        var folderNames = direct
            .Where(x => x.parts.Length > 1 || x.f.IsFolder)
            .Select(x => x.parts[0])
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folderNames)
        {
            children.Add(new { name = folder, isFolder = true, path = $"{prefix}{folder}" });
        }

        // files directly under prefix
        foreach (var item in direct.Where(x => x.parts.Length == 1 && !x.f.IsFolder))
        {
            children.Add(new
            {
                name = item.parts[0],
                isFolder = false,
                path = $"{prefix}{item.parts[0]}",
                size = item.f.SizeBytes
            });
        }

        return new JsonResult(children);
    }

    private static string NormalizePath(string path)
    {
        path ??= string.Empty;
        path = path.Replace("\\", "/");
        while (path.StartsWith("/")) path = path[1..];
        return path.Trim();
    }

    private static string CleanName(string input)
    {
        input = (input ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
        {
            input = input.Replace(c, '-');
        }
        input = input.Replace("/", "-").Replace("\\", "-");
        return input.Trim();
    }

    private string GetUploadsRoot()
    {
        var webRoot = _env.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
        }
        var lowerUploadsRoot = Path.Combine(webRoot, "uploads");
        var upperUploadsRoot = Path.Combine(webRoot, "Uploads");

        if (Directory.Exists(lowerUploadsRoot))
        {
            return lowerUploadsRoot;
        }

        if (Directory.Exists(upperUploadsRoot))
        {
            return upperUploadsRoot;
        }

        Directory.CreateDirectory(lowerUploadsRoot);
        return lowerUploadsRoot;
    }

    private string BuildPrimaryDomain(string slug)
    {
        var zone = _domainOptions.PrimaryZone?.Trim('.') ?? "bugence.app";
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "project";
        }
        return $"{slug}.{zone}".Trim('.');
    }

    private static void AddFileIndexEntries(int projectId, string rel, long sizeBytes, List<UploadedProjectFile> files)
    {
        files.Add(new UploadedProjectFile
        {
            UploadedProjectId = projectId,
            RelativePath = rel,
            SizeBytes = sizeBytes,
            IsFolder = false
        });

        var segments = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = string.Empty;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = string.IsNullOrEmpty(current) ? segments[i] : $"{current}/{segments[i]}";
            if (files.Any(f => f.IsFolder && f.RelativePath.Equals(current, StringComparison.OrdinalIgnoreCase)))
                continue;
            files.Add(new UploadedProjectFile
            {
                UploadedProjectId = projectId,
                RelativePath = current,
                SizeBytes = 0,
                IsFolder = true
            });
        }
    }

    private static void BuildFileIndex(int projectId, string projectRoot, List<UploadedProjectFile> files)
    {
        foreach (var path in Directory.GetFiles(projectRoot, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(projectRoot, path).Replace("\\", "/");
            var info = new FileInfo(path);
            AddFileIndexEntries(projectId, rel, info.Length, files);
        }
    }

    private static string SanitizeArchivePath(string path)
    {
        var clean = (path ?? string.Empty).Replace("\\", "/").Trim();
        while (clean.StartsWith("/")) clean = clean[1..];
        if (string.IsNullOrWhiteSpace(clean) || clean.Contains(".."))
        {
            return string.Empty;
        }
        return clean;
    }

    private static void FlattenSingleTopFolder(string projectRoot)
    {
        var topDirs = Directory.GetDirectories(projectRoot);
        var topFiles = Directory.GetFiles(projectRoot);
        if (topDirs.Length == 1 && topFiles.Length == 0)
        {
            var nestedRoot = topDirs[0];
            foreach (var entry in Directory.GetFileSystemEntries(nestedRoot))
            {
                var dest = Path.Combine(projectRoot, Path.GetFileName(entry));
                if (Directory.Exists(entry))
                {
                    if (Directory.Exists(dest))
                    {
                        foreach (var nested in Directory.GetFileSystemEntries(entry))
                        {
                            var nestedDest = Path.Combine(dest, Path.GetFileName(nested));
                            if (Directory.Exists(nested))
                            {
                                Directory.Move(nested, nestedDest);
                            }
                            else
                            {
                                System.IO.File.Move(nested, nestedDest, overwrite: true);
                            }
                        }
                        Directory.Delete(entry, recursive: true);
                    }
                    else
                    {
                        Directory.Move(entry, dest);
                    }
                }
                else
                {
                    System.IO.File.Move(entry, dest, overwrite: true);
                }
            }
            Directory.Delete(nestedRoot, recursive: true);
        }
    }

    private static async Task ExtractArchiveToProjectRootAsync(IFormFile archiveFile, string projectRoot)
    {
        await using var memory = new MemoryStream();
        await archiveFile.CopyToAsync(memory);
        memory.Position = 0;

        using var archive = ArchiveFactory.Open(memory);
        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
        {
            var clean = SanitizeArchivePath(entry.Key);
            if (string.IsNullOrWhiteSpace(clean)) continue;
            var destination = Path.Combine(projectRoot, clean.Replace("/", Path.DirectorySeparatorChar.ToString()));
            var fullDestination = Path.GetFullPath(destination);
            if (!fullDestination.StartsWith(Path.GetFullPath(projectRoot), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var destinationDir = Path.GetDirectoryName(fullDestination);
            if (!string.IsNullOrWhiteSpace(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }
            entry.WriteToFile(fullDestination, new ExtractionOptions { ExtractFullPath = false, Overwrite = true });
        }
    }

    private static string BuildPreviewUrl(int projectId, string relativePath)
    {
        var rel = Uri.EscapeDataString(relativePath.Replace("\\", "/").TrimStart('/'));
        return $"/Editor?handler=Preview&projectId={projectId}&file={rel}";
    }

    private static string ResolvePreviewUrl(UploadedProject project, IEnumerable<UploadedProjectFile> files)
    {
        var firstHtml = files
            .Where(f => f.UploadedProjectId == project.Id
                && !f.IsFolder
                && (f.RelativePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                    || f.RelativePath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f.RelativePath.ToLowerInvariant().Contains("index") ? 0 : 1)
            .ThenBy(f => f.RelativePath.Length)
            .FirstOrDefault();

        if (firstHtml == null)
        {
            return "#";
        }

        var rel = firstHtml.RelativePath.Replace("\\", "/");
        var prefix = project.FolderName.Replace("\\", "/").Trim('/') + "/";
        if (rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            rel = rel[prefix.Length..];
        }
        return BuildPreviewUrl(project.Id, rel);
    }

    private static long ResolveProjectSizeBytes(UploadedProject project, IEnumerable<UploadedProjectFile> files, string uploadsRoot)
    {
        if (project.SizeBytes > 0)
        {
            return project.SizeBytes;
        }

        var indexedSize = files
            .Where(f => f.UploadedProjectId == project.Id && !f.IsFolder)
            .Sum(f => f.SizeBytes);
        if (indexedSize > 0)
        {
            return indexedSize;
        }

        var projectRoot = Path.Combine(uploadsRoot, project.FolderName);
        return Directory.Exists(projectRoot) ? ComputeDirectorySize(projectRoot) : 0;
    }

    private static long ComputeDirectorySize(string rootPath)
    {
        long total = 0;
        foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(path).Length;
            }
            catch
            {
                // ignore transient read issues while summing directory size
            }
        }
        return total;
    }

    private static string FormatStorageSize(long sizeBytes)
    {
        var normalized = Math.Max(sizeBytes, 0);
        if (normalized < 1024)
        {
            return $"{normalized} B";
        }

        var kb = normalized / 1024d;
        if (kb < 1024)
        {
            return $"{kb:0.#} KB";
        }

        var mb = kb / 1024d;
        if (mb < 1024)
        {
            return $"{mb:0.#} MB";
        }

        var gb = mb / 1024d;
        return $"{gb:0.##} GB";
    }

    private static string NormalizeRelativePath(string rawPath, string rootFolder, string? requestedName)
    {
        var path = NormalizePath(rawPath);
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 1)
        {
            var first = segments[0];
            if (string.Equals(first, rootFolder, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(requestedName))
            {
                path = string.Join('/', segments.Skip(1));
            }
        }

        return path.TrimStart('/');
    }

    private async Task<UploadedProject?> CreateProjectAsync(List<IFormFile> upload, string? requestedName, string? statusOverride, object? meta, string? userId, Guid? companyId)
    {
        if (upload is null || upload.Count == 0) return null;
        companyId = await EnsureValidCompanyScopeAsync(userId, companyId);

        var first = upload[0];
        var relPath = NormalizePath(first.FileName);
        var isSingleArchive = upload.Count == 1 && (
            relPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            relPath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase));
        var topFolder = CleanName(requestedName);
        if (string.IsNullOrWhiteSpace(topFolder))
        {
            topFolder = relPath.Contains('/')
                ? relPath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                : Path.GetFileNameWithoutExtension(first.FileName);
        }
        if (string.IsNullOrWhiteSpace(topFolder))
            topFolder = "upload-" + Guid.NewGuid().ToString("N")[..6];

        var slugSeed = string.IsNullOrWhiteSpace(requestedName) ? topFolder : requestedName;
        var projectSlug = await SlugGenerator.GenerateProjectSlugAsync(_db, slugSeed ?? topFolder);

        var project = new UploadedProject
        {
            FolderName = topFolder,
            Slug = projectSlug,
            DisplayName = string.IsNullOrWhiteSpace(requestedName) ? topFolder : requestedName,
            OriginalFileName = first.FileName,
            SizeBytes = upload.Sum(f => f.Length),
            UploadedAtUtc = DateTime.UtcNow,
            Status = string.IsNullOrWhiteSpace(statusOverride) ? "Uploaded" : statusOverride,
            Data = meta is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(JsonSerializer.Serialize(meta)),
            UserId = userId,
            CompanyId = companyId
        };
        _db.UploadedProjects.Add(project);
        await _db.SaveChangesAsync();
        await _domainService.EnsurePrimaryDomainAsync(project.Id);

        var uploadsRoot = GetUploadsRoot();
        var projectRoot = Path.Combine(uploadsRoot, topFolder);
        Directory.CreateDirectory(projectRoot);

        var files = new List<UploadedProjectFile>();
        if (isSingleArchive)
        {
            await ExtractArchiveToProjectRootAsync(first, projectRoot);
            FlattenSingleTopFolder(projectRoot);
            BuildFileIndex(project.Id, projectRoot, files);
        }
        else
        {
            foreach (var file in upload)
            {
                var rel = NormalizeRelativePath(file.FileName, topFolder, requestedName);
                if (string.IsNullOrWhiteSpace(rel))
                {
                    continue;
                }

                var fullPath = Path.Combine(projectRoot, rel.Replace("/", Path.DirectorySeparatorChar.ToString()));
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                await using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                {
                    await file.CopyToAsync(stream);
                }

                AddFileIndexEntries(project.Id, rel, file.Length, files);
            }
        }

        var distinct = files
            .GroupBy(f => new { f.UploadedProjectId, f.RelativePath, f.IsFolder })
            .Select(g => g.First())
            .ToList();

        _db.UploadedProjectFiles.AddRange(distinct);
        await _db.SaveChangesAsync();
        return project;
    }

    private async Task<List<IFormFile>> ResolveUploadFilesAsync(List<IFormFile>? upload)
    {
        var files = (upload ?? new List<IFormFile>())
            .Where(f => f is not null)
            .ToList();
        if (files.Count > 0)
        {
            return files;
        }

        if (!Request.HasFormContentType)
        {
            return files;
        }

        var form = await Request.ReadFormAsync();
        if (form.Files.Count == 0)
        {
            return files;
        }

        return form.Files
            .Where(f => f is not null)
            .ToList();
    }

    private async Task<Guid?> EnsureValidCompanyScopeAsync(string? userId, Guid? companyId)
    {
        if (!companyId.HasValue)
        {
            return null;
        }

        var existing = await _db.CompanyProfiles.AnyAsync(c => c.Id == companyId.Value);
        if (existing)
        {
            return companyId;
        }

        string companyName = "Bugence Team";
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var user = await _userManager.FindByIdAsync(userId);
            var nameFromUser = user?.GetFriendlyName();
            if (!string.IsNullOrWhiteSpace(nameFromUser))
            {
                companyName = $"{nameFromUser} Team";
            }
            else if (!string.IsNullOrWhiteSpace(user?.Email))
            {
                var emailPrefix = user.Email.Split('@', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(emailPrefix))
                {
                    companyName = $"{emailPrefix} Team";
                }
            }
        }

        _db.CompanyProfiles.Add(new CompanyProfile
        {
            Id = companyId.Value,
            Name = companyName,
            CreatedByUserId = userId
        });

        try
        {
            await _db.SaveChangesAsync();
            return companyId;
        }
        catch (DbUpdateException)
        {
            var nowExists = await _db.CompanyProfiles.AnyAsync(c => c.Id == companyId.Value);
            return nowExists ? companyId : null;
        }
    }

    private async Task<(Guid? CompanyId, string? UserId)> GetCurrentCompanyScopeAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        return (user?.CompanyId, user?.Id);
    }

    private IQueryable<UploadedProject> GetScopedProjectsQuery(Guid? companyId, string? userId)
    {
        var query = _db.UploadedProjects.AsQueryable();
        if (companyId.HasValue)
        {
            return query.Where(p => p.CompanyId == companyId.Value);
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            return query.Where(p => p.UserId == userId);
        }

        return query.Where(_ => false);
    }

    private async Task<Dictionary<int, string>> GetEffectiveDomainsAsync(IEnumerable<UploadedProject> projects)
    {
        var projectList = projects?.ToList() ?? new List<UploadedProject>();
        var projectIds = projectList.Select(p => p.Id).Distinct().ToList();
        if (projectIds.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var domains = await _db.ProjectDomains
            .Where(d => projectIds.Contains(d.UploadedProjectId))
            .Select(d => new
            {
                d.UploadedProjectId,
                d.DomainName,
                d.DomainType,
                d.Status,
                d.SslStatus,
                d.UpdatedAtUtc
            })
            .ToListAsync();

        var map = new Dictionary<int, string>();
        foreach (var project in projectList)
        {
            var connectedCustom = domains
                .Where(d => d.UploadedProjectId == project.Id
                    && d.DomainType == ProjectDomainType.Custom
                    && d.Status == DomainStatus.Connected
                    && d.SslStatus == DomainSslStatus.Active)
                .OrderByDescending(d => d.UpdatedAtUtc)
                .Select(d => d.DomainName)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(connectedCustom))
            {
                map[project.Id] = connectedCustom;
                continue;
            }

            var primary = domains
                .Where(d => d.UploadedProjectId == project.Id && d.DomainType == ProjectDomainType.Primary)
                .OrderByDescending(d => d.UpdatedAtUtc)
                .Select(d => d.DomainName)
                .FirstOrDefault();

            map[project.Id] = !string.IsNullOrWhiteSpace(primary) ? primary : BuildPrimaryDomain(project.Slug);
        }

        return map;
    }

    private async Task<Dictionary<int, string>> GetConnectedCustomDomainsAsync(IEnumerable<UploadedProject> projects)
    {
        var projectIds = projects?
            .Select(p => p.Id)
            .Distinct()
            .ToList()
            ?? new List<int>();

        if (projectIds.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var connected = await _db.ProjectDomains
            .Where(d => projectIds.Contains(d.UploadedProjectId)
                && d.DomainType == ProjectDomainType.Custom
                && d.Status == DomainStatus.Connected
                && d.SslStatus == DomainSslStatus.Active)
            .Select(d => new
            {
                d.UploadedProjectId,
                d.DomainName,
                d.UpdatedAtUtc
            })
            .ToListAsync();

        return connected
            .GroupBy(d => d.UploadedProjectId)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(x => x.UpdatedAtUtc)
                    .Select(x => x.DomainName)
                    .First());
    }

    private async Task BackupProjectAsync(int projectId, string projectRoot)
    {
        if (!Directory.Exists(projectRoot))
        {
            return;
        }

        var backupDir = Path.Combine(_env.ContentRootPath, "App_Data", "restore", projectId.ToString());
        Directory.CreateDirectory(backupDir);
        var backupZip = Path.Combine(backupDir, "restore.zip");
        if (System.IO.File.Exists(backupZip))
        {
            System.IO.File.Delete(backupZip);
        }

        await Task.Run(() => ZipFile.CreateFromDirectory(projectRoot, backupZip, CompressionLevel.Fastest, includeBaseDirectory: false));
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDir))
        {
            var name = Path.GetFileName(directory);
            var nextDest = Path.Combine(destDir, name);
            Directory.CreateDirectory(nextDest);
            CopyDirectory(directory, nextDest);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            var dest = Path.Combine(destDir, name);
            System.IO.File.Copy(file, dest, overwrite: true);
        }
    }
}
