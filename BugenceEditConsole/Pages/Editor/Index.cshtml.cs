using BugenceEditConsole.Data;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BugenceEditConsole.Pages.Editor
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;
        private readonly DomainRoutingOptions _domainOptions;
        private readonly IProjectPublishService _projectPublishService;
        private readonly IProjectSnapshotService _snapshotService;
        private readonly IPreflightPublishService _preflightService;
        private readonly RepeaterTemplateService _repeaterService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(
            ApplicationDbContext db,
            IWebHostEnvironment env,
            IConfiguration config,
            IOptions<DomainRoutingOptions> domainOptions,
            RepeaterTemplateService repeaterService,
            UserManager<ApplicationUser> userManager,
            IProjectPublishService projectPublishService,
            IProjectSnapshotService snapshotService,
            IPreflightPublishService preflightService,
            ILogger<IndexModel> logger)
        {
            _db = db;
            _env = env;
            _config = config;
            _domainOptions = domainOptions.Value;
            _repeaterService = repeaterService;
            _userManager = userManager;
            _projectPublishService = projectPublishService;
            _snapshotService = snapshotService;
            _preflightService = preflightService;
            _logger = logger;
        }

        [BindProperty(SupportsGet = true)]
        public int? ProjectId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? File { get; set; }

        public string ProjectName { get; private set; } = "Project";
        public string FileName { get; private set; } = "index.html";
        public string FilePath { get; private set; } = "index.html";
        public string PreviewUrl { get; private set; } = "#";
        public string ResolvedPath { get; private set; } = string.Empty;

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

        private async Task<UploadedProject?> FindScopedProjectAsync(int projectId)
        {
            var (companyId, userId) = await GetCurrentCompanyScopeAsync();
            return await GetScopedProjectsQuery(companyId, userId).FirstOrDefaultAsync(p => p.Id == projectId);
        }

        public async Task OnGetAsync()
        {
            var (companyId, userId) = await GetCurrentCompanyScopeAsync();
            var scopedProjects = GetScopedProjectsQuery(companyId, userId);
            var project = await scopedProjects
                .OrderByDescending(p => p.UploadedAtUtc)
                .FirstOrDefaultAsync(p => ProjectId.HasValue && p.Id == ProjectId.Value)
                ?? await scopedProjects.OrderByDescending(p => p.UploadedAtUtc).FirstOrDefaultAsync();

            if (project == null)
            {
                return;
            }

            ProjectId = project.Id;
            ProjectName = project.FolderName;
            ResolvedPath = $"/Uploads/{project.FolderName}";
            EnsureProjectExtracted(project, project.FolderName);

            var files = await _db.UploadedProjectFiles
                .Where(f => f.UploadedProjectId == project.Id && !f.IsFolder)
                .ToListAsync();

            var requested = NormalizeProjectPath(project.FolderName, File);
            var selected = files.FirstOrDefault(f => f.RelativePath.Equals(requested, StringComparison.OrdinalIgnoreCase));
            if (selected == null)
            {
                selected = files.FirstOrDefault(f =>
                    f.RelativePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                    f.RelativePath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                    ?? files.FirstOrDefault();
            }

            if (selected != null)
            {
                var normalized = NormalizeProjectPath(project.FolderName, selected.RelativePath);
                FileName = Path.GetFileName(normalized);
                FilePath = normalized;
                PreviewUrl = BuildPreviewUrl(ProjectId ?? 0, normalized);
            }
            else
            {
                PreviewUrl = BuildPreviewUrl(ProjectId ?? 0, "index.html");
                FilePath = "index.html";
            }
        }

        public async Task<IActionResult> OnGetPreviewAsync(int projectId, string file)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null) return NotFound();

            EnsureProjectExtracted(project, project.FolderName);

            var cleanPath = NormalizeProjectPath(project.FolderName, file);
            if (string.IsNullOrWhiteSpace(cleanPath) || cleanPath.Contains(".."))
            {
                return BadRequest();
            }

            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var fullPath = Path.Combine(webRoot, "Uploads", project.FolderName, cleanPath.Replace("/", Path.DirectorySeparatorChar.ToString()));
            var draftPath = GetDraftPath(project, cleanPath);
            string html;
            if (System.IO.File.Exists(draftPath))
            {
                html = await System.IO.File.ReadAllTextAsync(draftPath, Encoding.UTF8);
            }
            else if (System.IO.File.Exists(fullPath))
            {
                html = await System.IO.File.ReadAllTextAsync(fullPath, Encoding.UTF8);
            }
            else
            {
                return NotFound();
            }
            var rewritten = RewriteHtml(html, projectId, project.FolderName, cleanPath);
            var cleaned = StripPreviewArtifacts(rewritten);
            if (!string.IsNullOrWhiteSpace(project.UserId))
            {
                cleaned = await _repeaterService.RenderAsync(cleaned, project.UserId, HttpContext.RequestAborted);
            }
            var injected = InjectEditorBridge(cleaned);
            return Content(injected, "text/html", Encoding.UTF8);
        }

        public async Task<IActionResult> OnGetAssetAsync(int projectId, string path)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null) return NotFound();

            EnsureProjectExtracted(project, project.FolderName);

            var cleanPath = NormalizeProjectPath(project.FolderName, path);
            if (string.IsNullOrWhiteSpace(cleanPath) || cleanPath.Contains(".."))
            {
                return BadRequest();
            }

            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var fullPath = ResolveAssetPath(webRoot, project.FolderName, cleanPath);
            var ext = Path.GetExtension(fullPath ?? cleanPath).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(fullPath) || !System.IO.File.Exists(fullPath))
            {
                return BuildMissingAssetResponse(ext, cleanPath);
            }
            if (ext == ".css")
            {
                var css = await System.IO.File.ReadAllTextAsync(fullPath, Encoding.UTF8);
                css = RewriteCssUrls(css, projectId, project.FolderName, cleanPath);
                return Content(css, "text/css", Encoding.UTF8);
            }

            var contentType = ext switch
            {
                ".js" => "text/javascript",
                ".json" => "application/json",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".svg" => "image/svg+xml",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                ".ttf" => "font/ttf",
                ".otf" => "font/otf",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".mp3" => "audio/mpeg",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };

            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
            return File(bytes, contentType);
        }

        public async Task<IActionResult> OnGetAssetsAsync(int projectId)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { items = Array.Empty<object>() });
            }

            var files = await _db.UploadedProjectFiles
                .Where(f => f.UploadedProjectId == projectId && !f.IsFolder)
                .ToListAsync();

            var items = files
                .Select(file =>
                {
                    var cleanPath = NormalizeProjectPath(project.FolderName, file.RelativePath);
                    var assetExt = Path.GetExtension(cleanPath).ToLowerInvariant();
                    var type = assetExt switch
                    {
                        ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".webp" => "image",
                        ".mp4" or ".webm" or ".mov" or ".m4v" => "video",
                        ".json" or ".lottie" => "lottie",
                        _ => "other"
                    };
                    if (type == "other") return null;
                    var url = $"/Editor?handler=Asset&projectId={projectId}&path={Uri.EscapeDataString(cleanPath)}";
                    return new
                    {
                        path = cleanPath,
                        url,
                        type,
                        name = Path.GetFileName(cleanPath)
                    };
                })
                .Where(item => item != null)
                .ToList();

            return new JsonResult(new { items });
        }

        public async Task<IActionResult> OnPostUploadAssetAsync(int projectId)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
            }

            EnsureProjectExtracted(project, project.FolderName);
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var uploadsRoot = Path.Combine(webRoot, "Uploads", project.FolderName);
            Directory.CreateDirectory(uploadsRoot);

            var files = Request.Form?.Files;
            if (files == null || files.Count == 0)
            {
                return new JsonResult(new { success = false, saved = 0 });
            }

            var savedItems = new List<object>();
            foreach (var file in files)
            {
                if (file == null || file.Length == 0) continue;
                var safeName = Path.GetFileName(file.FileName);
                if (string.IsNullOrWhiteSpace(safeName)) continue;
                var targetFolder = GetDefaultAssetFolder(safeName);
                var relativePath = NormalizePath(Path.Combine(targetFolder, safeName).Replace("\\", "/"));
                if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains("..")) continue;
                relativePath = EnsureUniqueRelativePath(uploadsRoot, relativePath);
                var destinationPath = Path.Combine(uploadsRoot, relativePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
                var destinationDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }
                await using (var stream = System.IO.File.Create(destinationPath))
                {
                    await file.CopyToAsync(stream);
                }
                savedItems.Add(new { path = relativePath, name = Path.GetFileName(relativePath) });
                await UpsertProjectFileRecordAsync(projectId, relativePath, file.Length);
            }

            await _db.SaveChangesAsync();
            return new JsonResult(new { success = true, saved = savedItems.Count, items = savedItems });
        }

        public async Task<IActionResult> OnPostDeleteAssetAsync(int projectId, [FromBody] DeleteAssetRequest payload)
        {
            if (payload == null || string.IsNullOrWhiteSpace(payload.Path))
            {
                return new JsonResult(new { success = false, message = "Invalid asset path." }) { StatusCode = 400 };
            }

            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
            }

            EnsureProjectExtracted(project, project.FolderName);
            var cleanPath = NormalizeProjectPath(project.FolderName, payload.Path);
            if (string.IsNullOrWhiteSpace(cleanPath) || cleanPath.Contains(".."))
            {
                return new JsonResult(new { success = false, message = "Invalid asset path." }) { StatusCode = 400 };
            }

            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var uploadsRoot = Path.Combine(webRoot, "Uploads", project.FolderName);
            var fullPath = Path.Combine(uploadsRoot, cleanPath.Replace("/", Path.DirectorySeparatorChar.ToString()));
            var targetPath = System.IO.File.Exists(fullPath)
                ? fullPath
                : ResolveCaseInsensitivePath(uploadsRoot, cleanPath);
            if (!string.IsNullOrWhiteSpace(targetPath) && System.IO.File.Exists(targetPath))
            {
                System.IO.File.Delete(targetPath);
            }

            var cleanLower = cleanPath.ToLowerInvariant();
            var existing = await _db.UploadedProjectFiles.FirstOrDefaultAsync(f =>
                f.UploadedProjectId == projectId &&
                f.RelativePath.ToLower() == cleanLower &&
                !f.IsFolder);
            if (existing != null)
            {
                _db.UploadedProjectFiles.Remove(existing);
                await _db.SaveChangesAsync();
            }

            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnGetEditorPrefsAsync(int projectId)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { });
            }

            EnsureProjectExtracted(project, project.FolderName);
            var prefsPath = GetProjectMetaPath(project, "bugence_editor_prefs.json");
            if (!System.IO.File.Exists(prefsPath))
            {
                return new JsonResult(new { });
            }

            var json = await System.IO.File.ReadAllTextAsync(prefsPath, Encoding.UTF8);
            return Content(json, "application/json", Encoding.UTF8);
        }

        public async Task<IActionResult> OnPostEditorPrefsAsync(int projectId, [FromBody] JsonElement payload)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
            }

            EnsureProjectExtracted(project, project.FolderName);
            var prefsPath = GetProjectMetaPath(project, "bugence_editor_prefs.json");
            var json = payload.ValueKind == JsonValueKind.Undefined
                ? "{}"
                : payload.GetRawText();
            await System.IO.File.WriteAllTextAsync(prefsPath, json, Encoding.UTF8);
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnGetMissingAssetsAsync(int projectId)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { count = 0, files = 0, items = Array.Empty<object>() });
            }

            EnsureProjectExtracted(project, project.FolderName);
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var uploadsRoot = Path.Combine(webRoot, "Uploads", project.FolderName);
            if (!Directory.Exists(uploadsRoot))
            {
                return new JsonResult(new { count = 0, files = 0, items = Array.Empty<object>() });
            }

            var (missing, fileCount) = await CollectMissingAssetsAsync(project, uploadsRoot, webRoot);
            var total = missing.Count;
            var items = missing
                .OrderBy(item => item.File, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ResolvedPath, StringComparer.OrdinalIgnoreCase)
                .Take(500)
                .ToList();

            return new JsonResult(new { count = total, files = fileCount, items });
        }

        public async Task<IActionResult> OnPostFixMissingAssetsAsync(int projectId)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
            }

            EnsureProjectExtracted(project, project.FolderName);
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var uploadsRoot = Path.Combine(webRoot, "Uploads", project.FolderName);
            if (!Directory.Exists(uploadsRoot))
            {
                return new JsonResult(new { success = true, @fixed = 0, remaining = 0, total = 0 });
            }

            var (missingItems, _) = await CollectMissingAssetsAsync(project, uploadsRoot, webRoot);
            if (missingItems.Count == 0)
            {
                return new JsonResult(new { success = true, @fixed = 0, remaining = 0, total = 0 });
            }

            var uniqueMissing = missingItems
                .GroupBy(item => item.ResolvedPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Where(item => !string.IsNullOrWhiteSpace(item.ResolvedPath))
                .ToList();

            var baseRoots = BuildFixBaseRoots(webRoot, _env.ContentRootPath);
            var searchRoots = BuildFixSearchRoots(baseRoots);
            var fileNameCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var fixedCount = 0;
            var fixedItems = new List<object>();
            var updatedRecords = false;

            foreach (var missing in uniqueMissing)
            {
                var resolved = missing.ResolvedPath;
                if (string.IsNullOrWhiteSpace(resolved) || resolved.Contains(".."))
                {
                    continue;
                }

                if (AssetExists(resolved, uploadsRoot, webRoot))
                {
                    continue;
                }

                var source = FindFixSourcePath(resolved, baseRoots, searchRoots, fileNameCache);
                if (string.IsNullOrWhiteSpace(source) || !System.IO.File.Exists(source))
                {
                    continue;
                }

                var destinationPath = Path.Combine(uploadsRoot, resolved.Replace("/", Path.DirectorySeparatorChar.ToString()));
                var destinationDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }
                if (!System.IO.File.Exists(destinationPath))
                {
                    System.IO.File.Copy(source, destinationPath, overwrite: false);
                }

                fixedCount++;
                fixedItems.Add(new { path = resolved, source = MakeRelativePath(source, baseRoots) });
                var relative = NormalizePath(resolved);
                await UpsertProjectFileRecordAsync(projectId, relative, new FileInfo(destinationPath).Length);
                updatedRecords = true;
            }

            if (updatedRecords)
            {
                await _db.SaveChangesAsync();
            }
            var remaining = uniqueMissing.Count(item => !AssetExists(item.ResolvedPath, uploadsRoot, webRoot));
            return new JsonResult(new
            {
                success = true,
                @fixed = fixedCount,
                remaining,
                total = uniqueMissing.Count,
                items = fixedItems.Take(200)
            });
        }

        public async Task<IActionResult> OnGetPagesAsync(int projectId)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { items = Array.Empty<PageMetaItem>() });
            }

            EnsureProjectExtracted(project, project.FolderName);
            var metaPath = GetProjectMetaPath(project, "bugence_pages.json");
            var metaItems = Array.Empty<PageMetaItem>();
            if (System.IO.File.Exists(metaPath))
            {
                try
                {
                    var raw = await System.IO.File.ReadAllTextAsync(metaPath, Encoding.UTF8);
                    var parsed = JsonSerializer.Deserialize<PageMetaPayload>(raw);
                    if (parsed?.Items != null)
                    {
                        metaItems = parsed.Items;
                    }
                }
                catch
                {
                    metaItems = Array.Empty<PageMetaItem>();
                }
            }

            var files = await _db.UploadedProjectFiles
                .Where(f => f.UploadedProjectId == projectId && !f.IsFolder)
                .ToListAsync();
            var htmlFiles = files
                .Where(f => f.RelativePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                    || f.RelativePath.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var items = htmlFiles.Select(file =>
            {
                var cleanPath = NormalizeProjectPath(project.FolderName, file.RelativePath);
                var match = metaItems.FirstOrDefault(item =>
                    !string.IsNullOrWhiteSpace(item.Path) &&
                    item.Path.Equals(cleanPath, StringComparison.OrdinalIgnoreCase));
                return new PageMetaItem
                {
                    Id = match?.Id ?? $"page-{cleanPath.Replace("/", "-")}",
                    Name = string.IsNullOrWhiteSpace(match?.Name)
                        ? Path.GetFileNameWithoutExtension(cleanPath)
                        : match!.Name,
                    Path = cleanPath,
                    Status = string.IsNullOrWhiteSpace(match?.Status) ? "draft" : match!.Status,
                    IsHome = match?.IsHome ?? false,
                    UpdatedAt = match?.UpdatedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
            }).ToList();

            if (items.Count > 0 && !items.Any(item => item.IsHome))
            {
                items[0].IsHome = true;
            }

            return new JsonResult(new { items });
        }

        public async Task<IActionResult> OnPostPagesAsync(int projectId, [FromBody] PageMetaPayload payload)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return BadRequest();
            }

            EnsureProjectExtracted(project, project.FolderName);
            var metaPath = GetProjectMetaPath(project, "bugence_pages.json");
            var json = payload?.Items == null
                ? JsonSerializer.Serialize(new PageMetaPayload { Items = Array.Empty<PageMetaItem>() })
                : JsonSerializer.Serialize(payload);
            await System.IO.File.WriteAllTextAsync(metaPath, json, Encoding.UTF8);
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostRenamePageAsync(int projectId, [FromBody] PageRenameRequest payload)
        {
            if (payload == null)
            {
                return BadRequest();
            }

            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
            }

            EnsureProjectExtracted(project, project.FolderName);
            var oldPath = NormalizeProjectPath(project.FolderName, payload.Path);
            var newPath = NormalizeProjectPath(project.FolderName, payload.NewPath);
            if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath) || oldPath.Contains("..") || newPath.Contains(".."))
            {
                return new JsonResult(new { success = false, message = "Invalid path." }) { StatusCode = 400 };
            }
            if (oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
            {
                return new JsonResult(new { success = true, renamed = false });
            }

            var oldFullPath = ResolveProjectFilePath(project, oldPath);
            var newFullPath = ResolveProjectFilePath(project, newPath);
            if (!System.IO.File.Exists(oldFullPath))
            {
                return new JsonResult(new { success = false, message = "Source file not found." }) { StatusCode = 404 };
            }
            if (System.IO.File.Exists(newFullPath))
            {
                return new JsonResult(new { success = false, message = "Destination already exists." }) { StatusCode = 409 };
            }

            var newDir = Path.GetDirectoryName(newFullPath);
            if (!string.IsNullOrWhiteSpace(newDir))
            {
                Directory.CreateDirectory(newDir);
            }
            System.IO.File.Move(oldFullPath, newFullPath);
            MoveDraftIfExists(project, oldPath, newPath);

            var existing = await _db.UploadedProjectFiles.FirstOrDefaultAsync(f =>
                f.UploadedProjectId == projectId &&
                f.RelativePath.ToLower() == oldPath.ToLowerInvariant() &&
                !f.IsFolder);
            if (existing != null)
            {
                existing.RelativePath = newPath;
                existing.SizeBytes = new FileInfo(newFullPath).Length;
            }
            else
            {
                _db.UploadedProjectFiles.Add(new UploadedProjectFile
                {
                    UploadedProjectId = projectId,
                    RelativePath = newPath,
                    SizeBytes = new FileInfo(newFullPath).Length,
                    IsFolder = false
                });
            }

            await _db.SaveChangesAsync();
            return new JsonResult(new { success = true, renamed = true, path = newPath });
        }

        public async Task<IActionResult> OnPostDuplicatePageAsync(int projectId, [FromBody] PageDuplicateRequest payload)
        {
            if (payload == null)
            {
                return BadRequest();
            }

            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
            }

            EnsureProjectExtracted(project, project.FolderName);
            var sourcePath = NormalizeProjectPath(project.FolderName, payload.SourcePath);
            var destinationPath = NormalizeProjectPath(project.FolderName, payload.DestinationPath);
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath) || sourcePath.Contains("..") || destinationPath.Contains(".."))
            {
                return new JsonResult(new { success = false, message = "Invalid path." }) { StatusCode = 400 };
            }

            var sourceFull = ResolveProjectFilePath(project, sourcePath);
            if (!System.IO.File.Exists(sourceFull))
            {
                return new JsonResult(new { success = false, message = "Source file not found." }) { StatusCode = 404 };
            }

            var destinationFull = ResolveProjectFilePath(project, destinationPath);
            if (System.IO.File.Exists(destinationFull))
            {
                return new JsonResult(new { success = false, message = "Destination already exists." }) { StatusCode = 409 };
            }

            var destinationDir = Path.GetDirectoryName(destinationFull);
            if (!string.IsNullOrWhiteSpace(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            var draftPath = GetDraftPath(project, sourcePath);
            string html;
            if (System.IO.File.Exists(draftPath))
            {
                html = await System.IO.File.ReadAllTextAsync(draftPath, Encoding.UTF8);
            }
            else
            {
                html = await System.IO.File.ReadAllTextAsync(sourceFull, Encoding.UTF8);
            }

            await System.IO.File.WriteAllTextAsync(destinationFull, html ?? string.Empty, Encoding.UTF8);

            var fileInfo = new FileInfo(destinationFull);
            _db.UploadedProjectFiles.Add(new UploadedProjectFile
            {
                UploadedProjectId = projectId,
                RelativePath = destinationPath,
                SizeBytes = fileInfo.Length,
                IsFolder = false
            });

            await _db.SaveChangesAsync();
            return new JsonResult(new { success = true, path = destinationPath });
        }

        public async Task<IActionResult> OnPostDeletePageAsync(int projectId, [FromBody] PageDeleteRequest payload)
        {
            if (payload == null)
            {
                return BadRequest();
            }

            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
            }

            EnsureProjectExtracted(project, project.FolderName);
            var cleanPath = NormalizeProjectPath(project.FolderName, payload.Path);
            if (string.IsNullOrWhiteSpace(cleanPath) || cleanPath.Contains(".."))
            {
                return new JsonResult(new { success = false, message = "Invalid path." }) { StatusCode = 400 };
            }

            var fullPath = ResolveProjectFilePath(project, cleanPath);
            if (System.IO.File.Exists(fullPath))
            {
                System.IO.File.Delete(fullPath);
            }
            DeleteDraftIfExists(project, cleanPath);

            var existing = await _db.UploadedProjectFiles.FirstOrDefaultAsync(f =>
                f.UploadedProjectId == projectId &&
                f.RelativePath.ToLower() == cleanPath.ToLowerInvariant() &&
                !f.IsFolder);
            if (existing != null)
            {
                _db.UploadedProjectFiles.Remove(existing);
                await _db.SaveChangesAsync();
            }

            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostAiAssistAsync(int projectId, [FromBody] AiAssistRequest payload)
        {
            if (payload == null)
            {
                return BadRequest();
            }

            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? _config["OpenAI:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new JsonResult(new { success = false, message = "AI provider not configured." }) { StatusCode = 400 };
            }

            var action = (payload.Action ?? "improve").Trim().ToLowerInvariant();
            var source = string.IsNullOrWhiteSpace(payload.Prompt) ? payload.Selection : payload.Prompt;
            if (string.IsNullOrWhiteSpace(source))
            {
                return new JsonResult(new { success = false, message = "No input provided." }) { StatusCode = 400 };
            }

            var systemPrompt = "You are a precise copywriter. Reply with only the rewritten text. No quotes, no markdown.";
            var instruction = action switch
            {
                "shorten" => "Shorten the text while keeping the key meaning.",
                "expand" => "Expand with one to two supportive details without changing the meaning.",
                "title" => "Convert the text to Title Case.",
                "bullets" => "Rewrite as concise bullet points, each starting with a dash.",
                _ => "Polish the text for clarity, tone, and flow."
            };

            var userPrompt = $"{instruction}\n\nText:\n{source}";
            if (!string.IsNullOrWhiteSpace(payload.Context))
            {
                userPrompt += $"\n\nContext (for tone only):\n{payload.Context}";
            }

            var model = _config["OpenAI:Model"] ?? "gpt-4o-mini";
            var requestBody = new
            {
                model,
                temperature = 0.4,
                max_tokens = 320,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            };

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            var response = await client.PostAsync(
                "https://api.openai.com/v1/chat/completions",
                new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));

            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return new JsonResult(new { success = false, message = "AI request failed." }) { StatusCode = (int)response.StatusCode };
            }

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var output = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();
                return new JsonResult(new { success = true, output = (output ?? string.Empty).Trim() });
            }
            catch
            {
                return new JsonResult(new { success = false, message = "AI response parsing failed." }) { StatusCode = 502 };
            }
        }

        public async Task<IActionResult> OnGetSymbolsAsync(int projectId)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { items = Array.Empty<object>() });
            }

            EnsureProjectExtracted(project, project.FolderName);
            var filePath = GetProjectMetaPath(project, "bugence_symbols.json");
            if (!System.IO.File.Exists(filePath))
            {
                return new JsonResult(new { items = Array.Empty<object>() });
            }

            var json = await System.IO.File.ReadAllTextAsync(filePath, Encoding.UTF8);
            return Content(json, "application/json", Encoding.UTF8);
        }

        public async Task<IActionResult> OnPostSymbolsAsync(int projectId, [FromBody] JsonElement payload)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return BadRequest();
            }

            EnsureProjectExtracted(project, project.FolderName);
            var filePath = GetProjectMetaPath(project, "bugence_symbols.json");
            var json = payload.ValueKind == JsonValueKind.Undefined
                ? JsonSerializer.Serialize(new { items = Array.Empty<object>() })
                : payload.GetRawText();
            await System.IO.File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnGetDesignTokensAsync(int projectId)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { presets = Array.Empty<object>(), pages = new { } });
            }

            EnsureProjectExtracted(project, project.FolderName);
            var filePath = GetProjectMetaPath(project, "bugence_design_tokens.json");
            if (!System.IO.File.Exists(filePath))
            {
                return new JsonResult(new { presets = Array.Empty<object>(), pages = new { } });
            }

            var json = await System.IO.File.ReadAllTextAsync(filePath, Encoding.UTF8);
            return Content(json, "application/json", Encoding.UTF8);
        }

        public async Task<IActionResult> OnPostDesignTokensAsync(int projectId, [FromBody] JsonElement payload)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return BadRequest();
            }

            EnsureProjectExtracted(project, project.FolderName);
            var filePath = GetProjectMetaPath(project, "bugence_design_tokens.json");
            var json = payload.ValueKind == JsonValueKind.Undefined
                ? JsonSerializer.Serialize(new { presets = Array.Empty<object>(), pages = new { } })
                : payload.GetRawText();
            await System.IO.File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostSavePageAsync(int projectId, string file, string html, bool overrideRisk = false)
        {
            return await SavePageInternalAsync(projectId, file, html, publish: false, overrideRisk);
        }

        public async Task<IActionResult> OnPostSaveDraftAsync(int projectId, string file, string html)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
            }

            EnsureProjectExtracted(project, project.FolderName);
            var cleanPath = NormalizeProjectPath(project.FolderName, file);
            if (string.IsNullOrWhiteSpace(cleanPath) || cleanPath.Contains(".."))
            {
                return new JsonResult(new { success = false, message = "Invalid path." }) { StatusCode = 400 };
            }

            var draftPath = GetDraftPath(project, cleanPath);
            var dir = Path.GetDirectoryName(draftPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
            await System.IO.File.WriteAllTextAsync(draftPath, html ?? string.Empty, Encoding.UTF8);
            return new JsonResult(new { success = true, draft = true });
        }

        public async Task<IActionResult> OnPostPublishPageAsync(int projectId, string file, string html, bool overrideRisk = false)
        {
            return await SavePageInternalAsync(projectId, file, html, publish: true, overrideRisk);
        }

        public async Task<IActionResult> OnPostPreflightPublishAsync(int projectId, string file, string html)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
            }

            EnsureProjectExtracted(project, project.FolderName);
            var cleanPath = NormalizeProjectPath(project.FolderName, file);
            if (string.IsNullOrWhiteSpace(cleanPath) || cleanPath.Contains(".."))
            {
                return new JsonResult(new { success = false, message = "Invalid path." }) { StatusCode = 400 };
            }

            var fullPath = ResolveProjectFilePath(project, cleanPath);
            var before = System.IO.File.Exists(fullPath)
                ? await System.IO.File.ReadAllTextAsync(fullPath, Encoding.UTF8)
                : string.Empty;
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var projectRoot = Path.Combine(webRoot, "Uploads", project.FolderName);
            var result = _preflightService.Evaluate(new PreflightPublishRequest
            {
                Project = project,
                FilePath = cleanPath,
                HtmlBefore = before,
                HtmlAfter = html ?? string.Empty,
                WebRootPath = webRoot,
                ProjectRootPath = projectRoot
            });

            _db.ProjectPreflightRuns.Add(new ProjectPreflightRun
            {
                UploadedProjectId = project.Id,
                FilePath = cleanPath,
                CreatedAtUtc = DateTime.UtcNow,
                Score = result.Score,
                Safe = result.Safe,
                BlockersJson = JsonSerializer.Serialize(result.Blockers),
                WarningsJson = JsonSerializer.Serialize(result.Warnings),
                DiffSummaryJson = JsonSerializer.Serialize(result.DiffSummary)
            });
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Preflight completed for project {ProjectId} safe={Safe} score={Score} blockers={Blockers} warnings={Warnings}",
                project.Id,
                result.Safe,
                result.Score,
                result.Blockers.Count,
                result.Warnings.Count);

            return new JsonResult(new
            {
                success = true,
                safe = result.Safe,
                score = result.Score,
                blockers = result.Blockers,
                warnings = result.Warnings,
                diffSummary = result.DiffSummary,
                changedAbsolute = result.ChangedAbsolute
            });
        }

        public async Task<IActionResult> OnPostPublishStatusAsync(int projectId, [FromBody] PublishStatusRequest payload)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
            }

            project.Status = payload?.Published == true ? "Published" : "Draft";
            await _db.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        private static string NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var trimmed = path;
            var queryIndex = trimmed.IndexOf('?');
            var hashIndex = trimmed.IndexOf('#');
            var cutIndex = queryIndex >= 0 && hashIndex >= 0 ? Math.Min(queryIndex, hashIndex)
                : (queryIndex >= 0 ? queryIndex : hashIndex);
            if (cutIndex >= 0)
            {
                trimmed = trimmed.Substring(0, cutIndex);
            }
            trimmed = trimmed.Replace("\\", "/");
            while (trimmed.StartsWith("/")) trimmed = trimmed[1..];
            return trimmed.Trim();
        }

        private string GetProjectMetaPath(UploadedProject project, string fileName)
        {
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var folder = Path.Combine(webRoot, "Uploads", project.FolderName, ".bugence");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, fileName);
        }

        private string GetDraftPath(UploadedProject project, string cleanPath)
        {
            var root = Path.Combine(_env.ContentRootPath, "App_Data", "bugence-drafts", project.FolderName);
            var relative = cleanPath.Replace("/", Path.DirectorySeparatorChar.ToString());
            return Path.Combine(root, relative);
        }

        private void DeleteDraftIfExists(UploadedProject project, string cleanPath)
        {
            var draftPath = GetDraftPath(project, cleanPath);
            if (System.IO.File.Exists(draftPath))
            {
                System.IO.File.Delete(draftPath);
            }
        }

        private void MoveDraftIfExists(UploadedProject project, string oldPath, string newPath)
        {
            var oldDraft = GetDraftPath(project, oldPath);
            if (!System.IO.File.Exists(oldDraft))
            {
                return;
            }
            var newDraft = GetDraftPath(project, newPath);
            var dir = Path.GetDirectoryName(newDraft);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
            if (System.IO.File.Exists(newDraft))
            {
                System.IO.File.Delete(newDraft);
            }
            System.IO.File.Move(oldDraft, newDraft);
        }

        private string ResolveProjectFilePath(UploadedProject project, string cleanPath)
        {
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            return Path.Combine(webRoot, "Uploads", project.FolderName, cleanPath.Replace("/", Path.DirectorySeparatorChar.ToString()));
        }

        private static string NormalizeProjectPath(string folderName, string? path)
        {
            var clean = NormalizePath(path);
            if (string.IsNullOrWhiteSpace(clean)) return clean;
            clean = StripUploadsPrefix(clean, folderName);
            var prefix = NormalizePath(folderName);
            if (!string.IsNullOrWhiteSpace(prefix) &&
                clean.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return clean.Substring(prefix.Length + 1);
            }
            return clean;
        }

        private void EnsureProjectExtracted(UploadedProject project, string folderName)
        {
            var webRoot = _env.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRoot))
            {
                webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
            }
            var uploadsRoot = Path.Combine(webRoot, "Uploads");
            var projectRoot = Path.Combine(uploadsRoot, folderName);
            if (Directory.Exists(projectRoot))
            {
                return;
            }
            if (project.Data == null || project.Data.Length == 0)
            {
                return;
            }
            Directory.CreateDirectory(projectRoot);
            try
            {
                using var stream = new MemoryStream(project.Data);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.FullName))
                    {
                        continue;
                    }
                    var sanitized = entry.FullName.Replace("\\", "/");
                    if (sanitized.StartsWith("/")) sanitized = sanitized[1..];
                    if (sanitized.Contains(".."))
                    {
                        continue;
                    }
                    var destinationPath = Path.Combine(projectRoot, sanitized.Replace("/", Path.DirectorySeparatorChar.ToString()));
                    var destinationDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrWhiteSpace(destinationDir))
                    {
                        Directory.CreateDirectory(destinationDir);
                    }
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(destinationPath);
                        continue;
                    }
                    var fullDestination = Path.GetFullPath(destinationPath);
                    if (!fullDestination.StartsWith(Path.GetFullPath(projectRoot), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    entry.ExtractToFile(fullDestination, overwrite: true);
                }
            }
            catch
            {
                // Best-effort extraction for editor preview.
            }
        }

        private static string BuildPreviewUrl(int projectId, string relativePath)
        {
            var cleanRel = NormalizePath(relativePath);
            if (string.IsNullOrWhiteSpace(cleanRel))
            {
                return $"/Editor?handler=Preview&projectId={projectId}";
            }
            return $"/Editor?handler=Preview&projectId={projectId}&file={Uri.EscapeDataString(cleanRel)}";
        }

        private static string RewriteHtml(string html, int projectId, string folderName, string htmlPath)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            html = RewriteAttributeValues(html, "href", projectId, folderName, htmlPath);
            html = RewriteAttributeValues(html, "src", projectId, folderName, htmlPath);
            html = RewriteAttributeValues(html, "srcset", projectId, folderName, htmlPath);
            html = RewriteAttributeValues(html, "poster", projectId, folderName, htmlPath);
            html = RewriteInlineUrls(html, projectId, folderName, htmlPath);
            html = InjectBaseHref(html, folderName, htmlPath);
            return html;
        }

        private class MissingAssetInfo
        {
            public string File { get; set; } = string.Empty;
            public string Reference { get; set; } = string.Empty;
            public string ResolvedPath { get; set; } = string.Empty;
        }

        private static readonly string[] AssetExtensions =
        {
            ".css", ".js", ".json", ".png", ".jpg", ".jpeg", ".svg", ".gif", ".webp",
            ".woff", ".woff2", ".ttf", ".otf", ".mp4", ".webm", ".mp3", ".pdf"
        };

        private static readonly string[] FixAssetRoots =
        {
            "Img", "Images", "Assets", "Css", "Script", "Fonts", "js", "css", "fonts"
        };

        private async Task<(List<MissingAssetInfo> Items, int FileCount)> CollectMissingAssetsAsync(
            UploadedProject project,
            string uploadsRoot,
            string webRoot)
        {
            if (!Directory.Exists(uploadsRoot))
            {
                return (new List<MissingAssetInfo>(), 0);
            }

            var htmlFiles = Directory.EnumerateFiles(uploadsRoot, "*.html", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(uploadsRoot, "*.htm", SearchOption.AllDirectories))
                .ToList();

            var missing = new List<MissingAssetInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in htmlFiles)
            {
                var relPath = Path.GetRelativePath(uploadsRoot, file).Replace("\\", "/");
                var draftPath = GetDraftPath(project, relPath);
                var html = System.IO.File.Exists(draftPath)
                    ? await System.IO.File.ReadAllTextAsync(draftPath, Encoding.UTF8)
                    : await System.IO.File.ReadAllTextAsync(file, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(html)) continue;

                var cleaned = StripScriptBlocks(html);
                foreach (var reference in EnumerateAssetReferences(cleaned, relPath, project.FolderName))
                {
                    if (string.IsNullOrWhiteSpace(reference.ResolvedPath)) continue;
                    var key = $"{relPath}::{reference.ResolvedPath}";
                    if (!seen.Add(key)) continue;
                    if (AssetExists(reference.ResolvedPath, uploadsRoot, webRoot)) continue;
                    missing.Add(new MissingAssetInfo
                    {
                        File = relPath,
                        Reference = reference.Reference,
                        ResolvedPath = reference.ResolvedPath
                    });
                }
            }

            return (missing, htmlFiles.Count);
        }

        private static List<string> BuildFixBaseRoots(string webRoot, string contentRoot)
        {
            var roots = new List<string>();
            void AddRoot(string? path)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                if (!Directory.Exists(path)) return;
                if (!roots.Any(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)))
                {
                    roots.Add(path);
                }
            }

            AddRoot(webRoot);
            AddRoot(contentRoot);
            var parent = Directory.GetParent(contentRoot)?.FullName;
            AddRoot(parent);
            return roots;
        }

        private static List<string> BuildFixSearchRoots(IEnumerable<string> baseRoots)
        {
            var roots = new List<string>();
            foreach (var baseRoot in baseRoots)
            {
                foreach (var folder in FixAssetRoots)
                {
                    var candidate = Path.Combine(baseRoot, folder);
                    if (Directory.Exists(candidate) && !roots.Any(existing => string.Equals(existing, candidate, StringComparison.OrdinalIgnoreCase)))
                    {
                        roots.Add(candidate);
                    }
                }
            }
            return roots;
        }

        private static string? FindFixSourcePath(
            string resolvedPath,
            IEnumerable<string> baseRoots,
            IReadOnlyCollection<string> searchRoots,
            IDictionary<string, string?> fileNameCache)
        {
            foreach (var root in baseRoots)
            {
                var match = ResolveCaseInsensitivePath(root, resolvedPath);
                if (!string.IsNullOrWhiteSpace(match) && System.IO.File.Exists(match))
                {
                    return match;
                }
            }

            var fileName = Path.GetFileName(resolvedPath);
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            if (fileNameCache.TryGetValue(fileName, out var cached))
            {
                return cached;
            }

            var segment = resolvedPath.Replace("\\", "/").Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var scopedRoots = searchRoots
                .Where(root => string.Equals(Path.GetFileName(root), segment, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (scopedRoots.Count == 0)
            {
                scopedRoots = searchRoots.ToList();
            }

            foreach (var root in scopedRoots)
            {
                if (!Directory.Exists(root)) continue;
                var match = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    fileNameCache[fileName] = match;
                    return match;
                }
            }

            fileNameCache[fileName] = null;
            return null;
        }

        private static string MakeRelativePath(string path, IEnumerable<string> roots)
        {
            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                var rootPath = Path.GetFullPath(root);
                var full = Path.GetFullPath(path);
                if (full.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    return full[rootPath.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
            }
            return path;
        }

        private static string StripScriptBlocks(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            return Regex.Replace(html, "<script\\b[^>]*>[\\s\\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
        }

        private static IEnumerable<(string Reference, string ResolvedPath)> EnumerateAssetReferences(string html, string htmlPath, string folderName)
        {
            var attrRegex = new Regex("\\b(src|href|poster)\\s*=\\s*(['\\\"])(.*?)\\2", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match match in attrRegex.Matches(html))
            {
                var attr = match.Groups[1].Value.ToLowerInvariant();
                var raw = WebUtility.HtmlDecode(match.Groups[3].Value).Trim();
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (attr == "href" && !ShouldCheckHref(raw)) continue;
                var resolved = NormalizeAssetReference(raw, htmlPath, folderName);
                if (string.IsNullOrWhiteSpace(resolved)) continue;
                yield return (raw, resolved);
            }

            var srcsetRegex = new Regex("\\bsrcset\\s*=\\s*(['\\\"])(.*?)\\1", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match match in srcsetRegex.Matches(html))
            {
                var rawSet = WebUtility.HtmlDecode(match.Groups[2].Value);
                var items = rawSet.Split(',');
                foreach (var item in items)
                {
                    var part = item.Trim();
                    if (string.IsNullOrWhiteSpace(part)) continue;
                    var segments = part.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    var url = segments[0];
                    var resolved = NormalizeAssetReference(url, htmlPath, folderName);
                    if (string.IsNullOrWhiteSpace(resolved)) continue;
                    yield return (url, resolved);
                }
            }

            var urlRegex = new Regex("url\\(([^)]+)\\)", RegexOptions.IgnoreCase);
            foreach (Match match in urlRegex.Matches(html))
            {
                var raw = WebUtility.HtmlDecode(match.Groups[1].Value).Trim().Trim('\"', '\'');
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var resolved = NormalizeAssetReference(raw, htmlPath, folderName);
                if (string.IsNullOrWhiteSpace(resolved)) continue;
                yield return (raw, resolved);
            }
        }

        private static string? NormalizeAssetReference(string value, string htmlPath, string folderName)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (value.Contains("${", StringComparison.Ordinal)) return null;
            var trimmed = value.Trim();
            if (IsSkippableReference(trimmed)) return null;
            var split = SplitUrlSuffix(trimmed);
            var baseValue = split.Base;

            if (baseValue.StartsWith("/Editor?handler=Asset", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate("http://localhost" + baseValue, UriKind.Absolute, out var uri))
                {
                    var query = QueryHelpers.ParseQuery(uri.Query);
                    if (query.TryGetValue("path", out var pathValue))
                    {
                        baseValue = pathValue.FirstOrDefault() ?? string.Empty;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(baseValue)) return null;
            if (baseValue.StartsWith("/"))
            {
                return StripUploadsPrefix(NormalizePath(baseValue), folderName);
            }
            var combined = CombineRelativePath(Path.GetDirectoryName(htmlPath) ?? string.Empty, baseValue);
            return StripUploadsPrefix(combined, folderName);
        }

        private static bool ShouldCheckHref(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (IsSkippableReference(value)) return false;
            var baseValue = SplitUrlSuffix(value).Base;
            var ext = Path.GetExtension(baseValue);
            if (string.IsNullOrWhiteSpace(ext)) return false;
            return AssetExtensions.Any(assetExt => ext.Equals(assetExt, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsSkippableReference(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            var trimmed = value.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal)) return true;
            if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("//", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool AssetExists(string cleanPath, string uploadsRoot, string webRoot)
        {
            if (string.IsNullOrWhiteSpace(cleanPath)) return true;
            var uploadMatch = ResolveCaseInsensitivePath(uploadsRoot, cleanPath);
            if (!string.IsNullOrWhiteSpace(uploadMatch) && System.IO.File.Exists(uploadMatch)) return true;
            var sharedMatch = ResolveCaseInsensitivePath(webRoot, cleanPath);
            if (!string.IsNullOrWhiteSpace(sharedMatch) && System.IO.File.Exists(sharedMatch)) return true;
            return false;
        }

        private static string StripPreviewArtifacts(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            html = Regex.Replace(html, "<script[^>]*src=[\"']/_vs/browserLink[^>]*>\\s*</script>", string.Empty, RegexOptions.IgnoreCase);
            html = Regex.Replace(html, "<script[^>]*src=[\"']/_framework/aspnetcore-browser-refresh\\.js[^>]*>\\s*</script>", string.Empty, RegexOptions.IgnoreCase);
            return html;
        }

        private static string InjectBaseHref(string html, string folderName, string htmlPath)
        {
            if (string.IsNullOrWhiteSpace(html)) return html;
            var safeFolder = NormalizePath(folderName);
            var normalizedPath = NormalizePath(htmlPath);
            var dir = Path.GetDirectoryName(normalizedPath)?.Replace("\\", "/") ?? string.Empty;
            var baseHref = string.IsNullOrWhiteSpace(dir)
                ? $"/Uploads/{safeFolder}/"
                : $"/Uploads/{safeFolder}/{dir.TrimEnd('/')}/";
            var baseTag = $"<base href=\"{baseHref}\" data-bugence-preview=\"true\">";
            var baseRegex = new Regex("<base\\b[^>]*>", RegexOptions.IgnoreCase);
            var baseMatch = baseRegex.Match(html);
            if (baseMatch.Success)
            {
                var originalTag = baseMatch.Value;
                var hrefMatch = Regex.Match(originalTag, "href\\s*=\\s*(['\\\"])(.*?)\\1", RegexOptions.IgnoreCase);
                if (hrefMatch.Success)
                {
                    var originalHref = WebUtility.HtmlEncode(hrefMatch.Groups[2].Value);
                    baseTag = $"<base href=\"{baseHref}\" data-bugence-preview=\"true\" data-bugence-original-href=\"{originalHref}\">";
                }
                return baseRegex.Replace(html, baseTag, 1);
            }
            var headMatch = Regex.Match(html, "<head\\b[^>]*>", RegexOptions.IgnoreCase);
            if (headMatch.Success)
            {
                return html.Insert(headMatch.Index + headMatch.Length, baseTag);
            }
            return baseTag + html;
        }

        private static string InjectEditorBridge(string html)
        {
            if (string.IsNullOrWhiteSpace(html) || html.Contains("bugence-editor-bridge"))
            {
                return html;
            }

            const string bridge = """
<style id="bugence-editor-bridge-style">
    .bugence-editor-outline {
        position: fixed;
        pointer-events: none;
        z-index: 2147483646;
        border: 2px solid rgba(6, 182, 212, 0.9);
        border-radius: 6px;
        box-shadow: 0 0 0 1px rgba(6, 182, 212, 0.35), 0 10px 30px rgba(0, 0, 0, 0.35);
        display: none;
        transition: opacity 0.08s ease;
        background: rgba(6, 182, 212, 0.06);
    }
    .bugence-editor-outline.bugence-editor-hover {
        border-style: dashed;
        background: rgba(6, 182, 212, 0.03);
    }
    .bugence-editor-grab {
        cursor: grab !important;
    }
</style>
<script id="bugence-editor-bridge">
(() => {
    const HOST_SOURCE = 'bugence-editor-host';
    const IFRAME_SOURCE = 'bugence-editor-iframe';
    const send = (type, payload) => {
        if (window.parent) {
            window.parent.postMessage({ source: IFRAME_SOURCE, type, payload }, '*');
        }
    };

    window.addEventListener('error', (event) => {
        if (!event) return;
        event.preventDefault();
        const message = event.message || 'Unknown error';
        const payload = {
            message,
            filename: event.filename || '',
            lineno: event.lineno || 0,
            colno: event.colno || 0
        };
        send('bugence-editor:preview-error', payload);
    });

    window.addEventListener('unhandledrejection', (event) => {
        if (!event) return;
        event.preventDefault();
        const reason = event.reason && (event.reason.message || event.reason.toString())
            ? (event.reason.message || event.reason.toString())
            : 'Unhandled promise rejection';
        send('bugence-editor:preview-error', { message: reason, filename: '', lineno: 0, colno: 0 });
    });

    const styleSafeAppend = (node) => {
        if (document.head) {
            document.head.appendChild(node);
        } else {
            document.documentElement.appendChild(node);
        }
    };

    const hoverBox = document.createElement('div');
    hoverBox.className = 'bugence-editor-outline bugence-editor-hover';
    hoverBox.setAttribute('data-bugence-editor', 'true');
    const selectBox = document.createElement('div');
    selectBox.className = 'bugence-editor-outline bugence-editor-selected';
    selectBox.setAttribute('data-bugence-editor', 'true');

    const styleNode = document.getElementById('bugence-editor-bridge-style');
    if (styleNode) styleSafeAppend(styleNode);
    const mountBoxes = () => {
        if (!document.body || hoverBox.isConnected) return;
        document.body.appendChild(hoverBox);
        document.body.appendChild(selectBox);
    };
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', mountBoxes);
    } else {
        mountBoxes();
    }

    let hovered = null;
    let selected = null;
    let grabTarget = null;
    let liveMode = true;

    const isEditorNode = (el) => !!(el && el.closest && el.closest('[data-bugence-editor="true"]'));
    const isSelectable = (el) => !!(el && el.nodeType === 1 && el !== document.body && el !== document.documentElement);

    const updateBox = (box, el) => {
        if (!box) return;
        if (!el) {
            box.style.display = 'none';
            return;
        }
        const rect = el.getBoundingClientRect();
        if (!rect || rect.width <= 0 || rect.height <= 0) {
            box.style.display = 'none';
            return;
        }
        box.style.display = 'block';
        box.style.top = rect.top + 'px';
        box.style.left = rect.left + 'px';
        box.style.width = rect.width + 'px';
        box.style.height = rect.height + 'px';
    };

    const refreshBoxes = () => {
        if (!liveMode) return;
        updateBox(hoverBox, hovered && hovered !== selected ? hovered : null);
        updateBox(selectBox, selected);
    };

    const updateGrabCursor = (next) => {
        if (grabTarget && grabTarget !== next) {
            grabTarget.classList.remove('bugence-editor-grab');
        }
        grabTarget = next;
        if (grabTarget) {
            grabTarget.classList.add('bugence-editor-grab');
        }
    };

    const setLiveMode = (enabled) => {
        liveMode = !!enabled;
        if (!liveMode) {
            hovered = null;
            selected = null;
            updateBox(hoverBox, null);
            updateBox(selectBox, null);
            updateGrabCursor(null);
        }
    };

    const escapeCss = (value) => {
        if (window.CSS && window.CSS.escape) return window.CSS.escape(value);
        return String(value).replace(/[^a-zA-Z0-9_-]/g, '\\\\$&');
    };

    const buildSelector = (el) => {
        const parts = [];
        let node = el;
        while (node && node.nodeType === 1 && node !== document.body) {
            const tag = node.tagName.toLowerCase();
            if (node.id) {
                parts.unshift(`${tag}#${escapeCss(node.id)}`);
                break;
            }
            let part = tag;
            const classes = (node.className || '').toString().trim().split(/\s+/).filter(Boolean);
            if (classes.length) {
                part += '.' + classes.slice(0, 3).map(escapeCss).join('.');
            }
            const parent = node.parentElement;
            if (parent) {
                const siblings = Array.from(parent.children).filter(child => child.tagName === node.tagName);
                if (siblings.length > 1) {
                    part += `:nth-of-type(${siblings.indexOf(node) + 1})`;
                }
            }
            parts.unshift(part);
            node = node.parentElement;
        }
        return parts.join(' > ');
    };

    const buildPayload = (el) => {
        const attrs = {};
        if (el && el.attributes) {
            Array.from(el.attributes).forEach(attr => {
                attrs[attr.name] = attr.value;
            });
        }
        const isFormField = ['input', 'textarea', 'select'].includes(el.tagName.toLowerCase());
        const textSource = isFormField ? (el.value || el.getAttribute('value') || '') : (el.textContent || '');
        const text = textSource.replace(/\s+/g, ' ').trim().slice(0, 240);
        const html = isFormField ? '' : (el.innerHTML || '');
        const rect = el.getBoundingClientRect();
        const computed = window.getComputedStyle ? window.getComputedStyle(el) : null;
        const computedStyles = computed ? {
            fontFamily: computed.fontFamily,
            fontSize: computed.fontSize,
            fontWeight: computed.fontWeight,
            color: computed.color,
            backgroundColor: computed.backgroundColor,
            textAlign: computed.textAlign,
            display: computed.display,
            marginTop: computed.marginTop,
            marginRight: computed.marginRight,
            marginBottom: computed.marginBottom,
            marginLeft: computed.marginLeft,
            paddingTop: computed.paddingTop,
            paddingRight: computed.paddingRight,
            paddingBottom: computed.paddingBottom,
            paddingLeft: computed.paddingLeft,
            borderRadius: computed.borderRadius
        } : {};
        return {
            tagName: el.tagName.toLowerCase(),
            id: el.id || '',
            className: el.className || '',
            text,
            html,
            attributes: attrs,
            selector: buildSelector(el),
            rect: { top: rect.top, left: rect.left, width: rect.width, height: rect.height },
            computedStyles
        };
    };

    const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
    const lerp = (a, b, t) => a + (b - a) * t;
    const easeValue = (easing, t) => {
        if (easing === 'ease-in-out') {
            return t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2;
        }
        if (easing === 'ease-out') {
            return 1 - Math.pow(1 - t, 3);
        }
        if (easing === 'ease') {
            return t * t * (3 - 2 * t);
        }
        return t;
    };
    const parseMotionData = (value) => {
        if (!value) return null;
        try {
            const parsed = JSON.parse(value);
            if (!parsed || typeof parsed !== 'object') return null;
            const tracks = Array.isArray(parsed.tracks) ? parsed.tracks : [];
            const scroll = parsed.scroll && typeof parsed.scroll === 'object' ? parsed.scroll : {};
            return {
                tracks,
                scroll: {
                    enabled: !!scroll.enabled,
                    start: typeof scroll.start === 'number' ? scroll.start : 10,
                    end: typeof scroll.end === 'number' ? scroll.end : 90,
                    scrub: typeof scroll.scrub === 'number' ? scroll.scrub : 1
                }
            };
        } catch {
            return null;
        }
    };
    const getMotionData = (el) => {
        if (!el) return null;
        const raw = el.getAttribute('data-bugence-motion');
        return parseMotionData(raw);
    };
    const applyMotionAtProgress = (el, progress) => {
        const motion = getMotionData(el);
        if (!motion || !motion.tracks?.length) return;
        const clamped = clamp(progress, 0, 100);
        const transforms = {};
        motion.tracks.forEach(track => {
            if (!track || !Array.isArray(track.keyframes) || !track.keyframes.length) return;
            const frames = track.keyframes.slice().sort((a, b) => a.t - b.t);
            let prev = frames[0];
            let next = frames[frames.length - 1];
            for (let i = 0; i < frames.length; i += 1) {
                if (clamped <= frames[i].t) {
                    next = frames[i];
                    prev = frames[i - 1] || frames[i];
                    break;
                }
            }
            const span = next.t - prev.t;
            const localT = span === 0 ? 0 : (clamped - prev.t) / span;
            const eased = easeValue(track.easing || 'linear', clamp(localT, 0, 1));
            const value = lerp(Number(prev.value || 0), Number(next.value || 0), eased);
            if (track.property === 'opacity') {
                el.style.opacity = value;
            } else if (track.property === 'translateY') {
                transforms.translateY = value;
            } else if (track.property === 'scale') {
                transforms.scale = value;
            } else if (track.property === 'rotate') {
                transforms.rotate = value;
            } else if (track.property) {
                el.style[track.property] = value;
            }
        });
        const transformParts = [];
        if (typeof transforms.translateY === 'number') {
            transformParts.push(`translateY(${transforms.translateY}px)`);
        }
        if (typeof transforms.scale === 'number') {
            transformParts.push(`scale(${transforms.scale})`);
        }
        if (typeof transforms.rotate === 'number') {
            transformParts.push(`rotate(${transforms.rotate}deg)`);
        }
        if (transformParts.length) {
            el.style.transform = transformParts.join(' ');
        }
    };
    const scrollProgressMap = new WeakMap();
    const updateScrollMotion = () => {
        const elements = Array.from(document.querySelectorAll('[data-bugence-motion]'));
        if (!elements.length) return;
        const viewHeight = window.innerHeight || document.documentElement.clientHeight || 1;
        elements.forEach(el => {
            const motion = getMotionData(el);
            if (!motion?.scroll?.enabled) return;
            const rect = el.getBoundingClientRect();
            const start = (motion.scroll.start ?? 10) / 100 * viewHeight;
            const end = (motion.scroll.end ?? 90) / 100 * viewHeight;
            const span = Math.max(1, Math.abs(start - end));
            const raw = start >= end
                ? (start - rect.top) / span
                : (rect.top - start) / span;
            let progress = clamp(raw * 100, 0, 100);
            const scrub = clamp(motion.scroll.scrub ?? 1, 0, 1);
            if (scrub < 1) {
                const last = scrollProgressMap.get(el) || 0;
                progress = last + (progress - last) * scrub;
                scrollProgressMap.set(el, progress);
            }
            applyMotionAtProgress(el, progress);
        });
    };
    let scrollRaf = null;
    const scheduleScrollMotion = () => {
        if (scrollRaf) return;
        scrollRaf = requestAnimationFrame(() => {
            scrollRaf = null;
            updateScrollMotion();
        });
    };
    window.addEventListener('scroll', scheduleScrollMotion, { passive: true });
    window.addEventListener('resize', scheduleScrollMotion);

    document.addEventListener('mousemove', (e) => {
        if (!liveMode) return;
        const target = e.target;
        if (!isSelectable(target) || isEditorNode(target)) return;
        if (hovered !== target) {
            hovered = target;
            updateBox(hoverBox, hovered && hovered !== selected ? hovered : null);
            updateGrabCursor(target);
        }
    }, true);

    document.addEventListener('mouseout', (e) => {
        if (!liveMode) return;
        if (e.target === hovered) {
            hovered = null;
            updateBox(hoverBox, null);
            updateGrabCursor(null);
        }
    }, true);

    document.addEventListener('click', (e) => {
        if (!liveMode) return;
        const target = e.target;
        if (!isSelectable(target) || isEditorNode(target)) return;
        e.preventDefault();
        e.stopPropagation();
        selected = target;
        updateBox(selectBox, selected);
        const payload = buildPayload(selected);
        if (e.shiftKey) {
            payload.multiAction = 'add';
        } else if (e.metaKey || e.ctrlKey) {
            payload.multiAction = 'toggle';
        } else {
            payload.multiAction = 'set';
        }
        send('bugence-editor:select', payload);
    }, true);

    window.addEventListener('scroll', refreshBoxes, true);
    window.addEventListener('resize', refreshBoxes);

    window.addEventListener('message', (event) => {
        const data = event.data || {};
        if (data.source !== HOST_SOURCE) return;
        if (data.type === 'bugence-editor:ping') {
            send('bugence-editor:ready');
        }
        if (data.type === 'bugence-editor:live-mode') {
            setLiveMode(data.payload?.enabled !== false);
        }
        if (data.type === 'bugence-editor:select' && data.payload && data.payload.selector) {
            const next = document.querySelector(data.payload.selector);
            if (next) {
                selected = next;
                updateBox(selectBox, selected);
                send('bugence-editor:select', buildPayload(selected));
            }
        }
        if (data.type === 'bugence-editor:update' && data.payload && selected) {
            const styles = data.payload.styles || {};
            const attributes = data.payload.attributes || {};
            const tagName = selected.tagName.toLowerCase();
            if (['input', 'textarea'].includes(tagName)) {
                const nextValue = typeof data.payload.text === 'string'
                    ? data.payload.text
                    : (typeof data.payload.html === 'string' ? data.payload.html : null);
                if (nextValue !== null) {
                    selected.value = nextValue;
                    selected.setAttribute('value', nextValue);
                }
            } else if (typeof data.payload.html === 'string') {
                selected.innerHTML = data.payload.html;
            } else if (typeof data.payload.text === 'string') {
                selected.textContent = data.payload.text;
            }
            Object.entries(attributes).forEach(([name, value]) => {
                if (value === null || value === undefined || value === '') {
                    selected.removeAttribute(name);
                } else {
                    selected.setAttribute(name, value);
                }
            });
            Object.entries(styles).forEach(([name, value]) => {
                if (value === null || value === undefined) return;
                selected.style[name] = value;
            });
            send('bugence-editor:select', buildPayload(selected));
        }
        if (data.type === 'bugence-editor:motion-update' && data.payload?.selector) {
            const target = document.querySelector(data.payload.selector);
            if (target) {
                const motion = data.payload.motion || {};
                target.setAttribute('data-bugence-motion', JSON.stringify(motion));
                applyMotionAtProgress(target, 0);
                send('bugence-editor:select', buildPayload(target));
                scheduleScrollMotion();
            }
        }
        if (data.type === 'bugence-editor:motion-preview' && data.payload?.selector) {
            const target = document.querySelector(data.payload.selector);
            if (target) {
                applyMotionAtProgress(target, Number(data.payload.progress || 0));
            }
        }
        if (data.type === 'bugence-editor:lottie-apply' && data.payload?.selector) {
            const target = document.querySelector(data.payload.selector);
            const url = data.payload.url || '';
            if (target && url) {
                const ensureScript = () => {
                    if (document.querySelector('script[src*="lottie-player"]')) return;
                    const script = document.createElement('script');
                    script.src = 'https://unpkg.com/@lottiefiles/lottie-player@latest/dist/lottie-player.js';
                    document.head.appendChild(script);
                };
                ensureScript();
                let player = target;
                if (target.tagName.toLowerCase() !== 'lottie-player') {
                    target.innerHTML = '';
                    player = document.createElement('lottie-player');
                    target.appendChild(player);
                }
                player.setAttribute('src', url);
                player.setAttribute('background', 'transparent');
                player.setAttribute('speed', '1');
                player.style.width = '100%';
                player.style.height = '100%';
                if (data.payload.loop) {
                    player.setAttribute('loop', '');
                } else {
                    player.removeAttribute('loop');
                }
                if (data.payload.autoplay) {
                    player.setAttribute('autoplay', '');
                } else {
                    player.removeAttribute('autoplay');
                }
                target.setAttribute('data-bugence-type', 'lottie');
                target.setAttribute('data-bugence-lottie', url);
                send('bugence-editor:select', buildPayload(target));
            }
        }
        if (data.type === 'bugence-editor:lottie-seek' && data.payload?.selector) {
            const target = document.querySelector(data.payload.selector);
            if (!target) return;
            const player = target.tagName.toLowerCase() === 'lottie-player'
                ? target
                : target.querySelector('lottie-player');
            if (!player) return;
            const progress = Number(data.payload.progress || 0) / 100;
            if (typeof player.seek === 'function') {
                player.seek(progress * 100);
            } else if (typeof player.setSeeker === 'function') {
                player.setSeeker(progress * 100);
            }
        }
    });

    send('bugence-editor:ready');
    scheduleScrollMotion();
})();
</script>
""";

            var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyClose >= 0)
            {
                return html.Insert(bodyClose, bridge);
            }
            var htmlClose = html.LastIndexOf("</html>", StringComparison.OrdinalIgnoreCase);
            if (htmlClose >= 0)
            {
                return html.Insert(htmlClose, bridge);
            }
            return html + bridge;
        }

        private static string RewritePath(string path, int projectId, string folderName)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            if (path == "/") return path;
            var clean = StripUploadsPrefix(NormalizePath(path), folderName);
            if (clean.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || clean.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
            {
                return $"/Editor?handler=Preview&projectId={projectId}&file={Uri.EscapeDataString(clean)}";
            }
            return BuildUploadsPath(clean, folderName);
        }

        private static string RewriteInlineUrls(string html, int projectId, string folderName, string htmlPath)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            var sb = new StringBuilder(html.Length);
            var idx = 0;
            var tagRegex = new Regex("<script\\b[^>]*>", RegexOptions.IgnoreCase);
            while (idx < html.Length)
            {
                var match = tagRegex.Match(html, idx);
                if (!match.Success)
                {
                    sb.Append(RewriteInlineUrlsSegment(html.Substring(idx), projectId, folderName, htmlPath));
                    break;
                }
                if (match.Index > idx)
                {
                    sb.Append(RewriteInlineUrlsSegment(html.Substring(idx, match.Index - idx), projectId, folderName, htmlPath));
                }
                var closeTag = "</script>";
                var closeIndex = html.IndexOf(closeTag, match.Index + match.Length, StringComparison.OrdinalIgnoreCase);
                if (closeIndex < 0)
                {
                    sb.Append(html.Substring(match.Index));
                    break;
                }
                var closeEnd = html.IndexOf('>', closeIndex);
                if (closeEnd < 0)
                {
                    sb.Append(html.Substring(match.Index));
                    break;
                }
                sb.Append(html.Substring(match.Index, closeEnd - match.Index + 1));
                idx = closeEnd + 1;
            }
            return sb.ToString();
        }

        private static string RewriteInlineUrlsSegment(string html, int projectId, string folderName, string htmlPath)
        {
            if (string.IsNullOrWhiteSpace(html)) return html;
            return Regex.Replace(html, "url\\(([^)]+)\\)", match =>
            {
                var raw = match.Groups[1].Value.Trim();
                if (string.IsNullOrWhiteSpace(raw)) return match.Value;
                var quote = string.Empty;
                if ((raw.StartsWith("'") && raw.EndsWith("'")) || (raw.StartsWith("\"") && raw.EndsWith("\"")))
                {
                    quote = raw[..1];
                    raw = raw.Substring(1, raw.Length - 2);
                }
                var path = raw.Trim();
                if (string.IsNullOrWhiteSpace(path)) return match.Value;
                if (path.StartsWith("#", StringComparison.Ordinal) ||
                    path.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("//", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("var(", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("calc(", StringComparison.OrdinalIgnoreCase))
                {
                    return match.Value;
                }
                var rewritten = RewriteAssetPathValue(path, projectId, folderName, htmlPath);
                return $"url({quote}{rewritten}{quote})";
            }, RegexOptions.IgnoreCase);
        }

        private static string RewriteCssUrls(string css, int projectId, string folderName, string cssPath)
        {
            if (string.IsNullOrWhiteSpace(css)) return string.Empty;
            var marker = "url(";
            var idx = 0;
            var sb = new StringBuilder(css.Length);
            while (true)
            {
                var pos = css.IndexOf(marker, idx, StringComparison.OrdinalIgnoreCase);
                if (pos < 0) break;
                var start = pos + marker.Length;
                var end = css.IndexOf(")", start, StringComparison.OrdinalIgnoreCase);
                if (end < 0) break;
                var raw = css.Substring(start, end - start).Trim('\"', '\'', ' ');
                var rewritten = RewriteAssetPathValue(raw, projectId, folderName, cssPath);
                sb.Append(css, idx, pos - idx);
                sb.Append($"url({rewritten})");
                idx = end + 1;
            }
            sb.Append(css, idx, css.Length - idx);
            return sb.ToString();
        }

        private static string RewriteAttributeValues(string html, string attr, int projectId, string folderName, string htmlPath)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            var sb = new StringBuilder(html.Length);
            var idx = 0;
            while (idx < html.Length)
            {
                var tagStart = html.IndexOf('<', idx);
                if (tagStart < 0)
                {
                    sb.Append(html, idx, html.Length - idx);
                    break;
                }
                sb.Append(html, idx, tagStart - idx);
                var tagEnd = html.IndexOf('>', tagStart);
                if (tagEnd < 0)
                {
                    sb.Append(html, tagStart, html.Length - tagStart);
                    break;
                }
                var tag = html.Substring(tagStart, tagEnd - tagStart + 1);
                if (tag.StartsWith("<script", StringComparison.OrdinalIgnoreCase) ||
                    tag.StartsWith("<style", StringComparison.OrdinalIgnoreCase))
                {
                    var closeTag = tag.StartsWith("<script", StringComparison.OrdinalIgnoreCase) ? "</script>" : "</style>";
                    var closeIndex = html.IndexOf(closeTag, tagEnd + 1, StringComparison.OrdinalIgnoreCase);
                    if (closeIndex < 0)
                    {
                        sb.Append(RewriteAttributeInTag(tag, attr, projectId, folderName, htmlPath));
                        idx = tagEnd + 1;
                        continue;
                    }
                    var closeEnd = html.IndexOf('>', closeIndex);
                    if (closeEnd < 0)
                    {
                        sb.Append(html, tagStart, html.Length - tagStart);
                        break;
                    }
                    var rewrittenTag = RewriteAttributeInTag(tag, attr, projectId, folderName, htmlPath);
                    sb.Append(rewrittenTag);
                    sb.Append(html, tagEnd + 1, closeEnd - tagEnd);
                    idx = closeEnd + 1;
                    continue;
                }
                sb.Append(RewriteAttributeInTag(tag, attr, projectId, folderName, htmlPath));
                idx = tagEnd + 1;
            }
            return sb.ToString();
        }

        private static string RewriteAttributeInTag(string tag, string attr, int projectId, string folderName, string htmlPath)
        {
            if (string.IsNullOrWhiteSpace(tag)) return tag;
            var pattern = $"{attr}\\s*=\\s*(['\\\"])(.*?)\\1";
            return Regex.Replace(tag, pattern, match =>
            {
                var quote = match.Groups[1].Value;
                var value = match.Groups[2].Value;
                var rewritten = attr.Equals("srcset", StringComparison.OrdinalIgnoreCase)
                    ? RewriteSrcSetValue(value, projectId, folderName, htmlPath)
                    : RewriteLinkOrAsset(value, projectId, folderName, htmlPath);
                return $"{attr}={quote}{rewritten}{quote}";
            }, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        private static string RewriteSrcSetValue(string value, int projectId, string folderName, string htmlPath)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            var parts = value.Split(',');
            var rewrittenParts = parts.Select(part =>
            {
                var trimmed = part.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) return trimmed;
                var segments = trimmed.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                var url = segments[0];
                var descriptor = segments.Length > 1 ? " " + segments[1] : string.Empty;
                var rewrittenUrl = RewriteLinkOrAsset(url, projectId, folderName, htmlPath);
                return rewrittenUrl + descriptor;
            });
            return string.Join(", ", rewrittenParts);
        }

        private static (string Base, string Suffix) SplitUrlSuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return (value, string.Empty);
            var queryIndex = value.IndexOf('?');
            var hashIndex = value.IndexOf('#');
            var cutIndex = queryIndex >= 0 && hashIndex >= 0 ? Math.Min(queryIndex, hashIndex)
                : (queryIndex >= 0 ? queryIndex : hashIndex);
            if (cutIndex < 0) return (value, string.Empty);
            return (value.Substring(0, cutIndex), value.Substring(cutIndex));
        }

        private static string RewriteLinkOrAsset(string value, int projectId, string folderName, string htmlPath)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            if (value.Contains("${", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            if (value.StartsWith("/Editor?handler=", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
            if (value.StartsWith("#", StringComparison.Ordinal) ||
                value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("//", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            var split = SplitUrlSuffix(value);
            var baseValue = split.Base;
            if (baseValue.StartsWith("/"))
            {
                if (ShouldPreserveAppAbsolutePath(baseValue))
                {
                    return baseValue + split.Suffix;
                }
                return RewritePath(baseValue, projectId, folderName) + split.Suffix;
            }

            var combined = CombineRelativePath(Path.GetDirectoryName(htmlPath) ?? string.Empty, baseValue);
            return RewritePath(combined, projectId, folderName) + split.Suffix;
        }

        private static bool ShouldPreserveAppAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase)
                   || path.Equals("/js", StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase)
                   || path.Equals("/css", StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith("/lib/", StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith("/fonts/", StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith("/img/", StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith("/images/", StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith("/_content/", StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith("/_framework/", StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith("/manifest", StringComparison.OrdinalIgnoreCase)
                   || path.StartsWith("/site.webmanifest", StringComparison.OrdinalIgnoreCase);
        }

        private static string RewriteAssetPathValue(string value, int projectId, string folderName, string basePath)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            if (value.Contains("${", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            if (value.StartsWith("/Editor?handler=", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
            if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("//", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
            var split = SplitUrlSuffix(value);
            var baseValue = split.Base;
            if (baseValue.StartsWith("/"))
            {
                var clean = StripUploadsPrefix(NormalizePath(baseValue), folderName);
                return BuildUploadsPath(clean, folderName) + split.Suffix;
            }

            var combined = CombineRelativePath(Path.GetDirectoryName(basePath) ?? string.Empty, baseValue);
            combined = StripUploadsPrefix(combined, folderName);
            return BuildUploadsPath(combined, folderName) + split.Suffix;
        }

        private static string BuildUploadsPath(string cleanPath, string folderName)
        {
            var safeFolder = NormalizePath(folderName);
            var normalized = StripUploadsPrefix(NormalizePath(cleanPath), folderName).TrimStart('/');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return $"/Uploads/{safeFolder}/";
            }
            return $"/Uploads/{safeFolder}/{normalized}";
        }

        private static string CombineRelativePath(string baseDir, string relative)
        {
            var baseParts = NormalizePath(baseDir).Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            var rel = relative.Split(new[] { '?', '#' }, 2)[0];
            var relParts = rel.Replace("\\", "/").Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in relParts)
            {
                if (part == ".") continue;
                if (part == "..")
                {
                    if (baseParts.Count > 0) baseParts.RemoveAt(baseParts.Count - 1);
                    continue;
                }
                baseParts.Add(part);
            }
            return string.Join("/", baseParts);
        }

        private static string StripUploadsPrefix(string path, string folderName)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            var clean = path.Replace("\\", "/").TrimStart('/');
            var folder = NormalizePath(folderName);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                if (clean.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return clean[(folder.Length + 1)..];
                }
                if (clean.StartsWith("uploads/" + folder + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return clean[(("uploads/" + folder + "/").Length)..];
                }
                if (clean.StartsWith("Uploads/" + folder + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return clean[(("Uploads/" + folder + "/").Length)..];
                }
            }
            return clean;
        }

        private static string? ResolveCaseInsensitivePath(string rootPath, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(relativePath)) return null;
            var current = rootPath;
            var parts = relativePath.Replace("\\", "/").Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (!Directory.Exists(current)) return null;
                var match = Directory.EnumerateFileSystemEntries(current)
                    .FirstOrDefault(entry => string.Equals(Path.GetFileName(entry), part, StringComparison.OrdinalIgnoreCase));
                if (match == null) return null;
                current = match;
            }
            return current;
        }

        private string? ResolveAssetPath(string webRoot, string folderName, string cleanPath)
        {
            var relative = cleanPath.Replace("/", Path.DirectorySeparatorChar.ToString());
            var fullPath = Path.Combine(webRoot, "Uploads", folderName, relative);
            if (System.IO.File.Exists(fullPath))
            {
                return fullPath;
            }

            var uploadsRoot = Path.Combine(webRoot, "Uploads", folderName);
            var insensitive = ResolveCaseInsensitivePath(uploadsRoot, cleanPath);
            if (!string.IsNullOrWhiteSpace(insensitive) && System.IO.File.Exists(insensitive))
            {
                return insensitive;
            }

            if (cleanPath.StartsWith("fonts/", StringComparison.OrdinalIgnoreCase))
            {
                var fontName = Path.GetFileName(cleanPath);
                var alt = Path.Combine(webRoot, "Uploads", folderName, "norwester", "norwester-v1.2", "webfonts", fontName);
                if (System.IO.File.Exists(alt))
                {
                    return alt;
                }
            }

            if (cleanPath.Equals("norwester.woff", StringComparison.OrdinalIgnoreCase))
            {
                var alt = Path.Combine(webRoot, "Uploads", folderName, "norwester", "norwester-v1.2", "webfonts", "norwester.woff");
                if (System.IO.File.Exists(alt))
                {
                    return alt;
                }
            }

            var sharedPath = Path.Combine(webRoot, relative);
            if (System.IO.File.Exists(sharedPath))
            {
                return sharedPath;
            }
            var sharedInsensitive = ResolveCaseInsensitivePath(webRoot, cleanPath);
            if (!string.IsNullOrWhiteSpace(sharedInsensitive) && System.IO.File.Exists(sharedInsensitive))
            {
                return sharedInsensitive;
            }

            return fullPath;
        }

        private static string GetDefaultAssetFolder(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext)) return "Assets";
            if (ext is ".png" or ".jpg" or ".jpeg" or ".svg" or ".gif" or ".webp")
            {
                return "Img";
            }
            if (ext is ".css")
            {
                return "Css";
            }
            if (ext is ".js" or ".mjs")
            {
                return "Script";
            }
            if (ext is ".woff" or ".woff2" or ".ttf" or ".otf")
            {
                return "Fonts";
            }
            return "Assets";
        }

        private static string EnsureUniqueRelativePath(string rootPath, string relativePath)
        {
            var normalized = NormalizePath(relativePath);
            var full = Path.Combine(rootPath, normalized.Replace("/", Path.DirectorySeparatorChar.ToString()));
            if (!System.IO.File.Exists(full))
            {
                return normalized;
            }
            var directory = Path.GetDirectoryName(normalized)?.Replace("\\", "/") ?? string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(normalized);
            var ext = Path.GetExtension(normalized);
            for (var i = 1; i < 2000; i++)
            {
                var candidateName = $"{fileName}-{i}{ext}";
                var candidatePath = string.IsNullOrWhiteSpace(directory)
                    ? candidateName
                    : $"{directory}/{candidateName}";
                var candidateFull = Path.Combine(rootPath, candidatePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
                if (!System.IO.File.Exists(candidateFull))
                {
                    return candidatePath;
                }
            }
            return normalized;
        }

        private async Task UpsertProjectFileRecordAsync(int projectId, string relativePath, long sizeBytes)
        {
            var cleanLower = relativePath.ToLowerInvariant();
            var existing = await _db.UploadedProjectFiles.FirstOrDefaultAsync(f =>
                f.UploadedProjectId == projectId &&
                f.RelativePath.ToLower() == cleanLower &&
                !f.IsFolder);
            if (existing == null)
            {
                _db.UploadedProjectFiles.Add(new UploadedProjectFile
                {
                    UploadedProjectId = projectId,
                    RelativePath = relativePath,
                    SizeBytes = sizeBytes,
                    IsFolder = false
                });
            }
            else
            {
                existing.SizeBytes = sizeBytes;
            }
        }

        private void PublishProjectSnapshot(UploadedProject project)
        {
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var sourceRoot = Path.Combine(webRoot, "Uploads", project.FolderName);
            if (!Directory.Exists(sourceRoot)) return;

            var publishRoot = string.IsNullOrWhiteSpace(_domainOptions.PublishRoot)
                ? "Published"
                : _domainOptions.PublishRoot.Trim('\\', '/', ' ');

            var slugTarget = Path.Combine(webRoot, publishRoot, "slugs", project.Slug);
            var projectTarget = Path.Combine(webRoot, publishRoot, "projects", project.Id.ToString());

            PublishDirectory(sourceRoot, slugTarget);
            PublishDirectory(sourceRoot, projectTarget);

            project.PublishStoragePath = Path.Combine(publishRoot, "slugs", project.Slug);
            project.LastPublishedAtUtc = DateTime.UtcNow;
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
                if (string.Equals(name, ".bugence", StringComparison.OrdinalIgnoreCase)) continue;
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

        private static IActionResult BuildMissingAssetResponse(string ext, string cleanPath)
        {
            var contentType = ext switch
            {
                ".css" => "text/css",
                ".js" => "text/javascript",
                ".json" => "application/json",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".svg" => "image/svg+xml",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                ".ttf" => "font/ttf",
                ".otf" => "font/otf",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".mp3" => "audio/mpeg",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };

            if (ext == ".css")
            {
                return new ContentResult
                {
                    ContentType = contentType,
                    Content = $"/* Missing asset: {cleanPath} */",
                    StatusCode = 200
                };
            }
            if (ext == ".js")
            {
                return new ContentResult
                {
                    ContentType = contentType,
                    Content = $"/* Missing asset: {cleanPath} */",
                    StatusCode = 200
                };
            }
            if (ext == ".svg" || ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".webp")
            {
                var label = string.IsNullOrWhiteSpace(cleanPath) ? "Missing asset" : Path.GetFileName(cleanPath);
                var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"640\" height=\"360\" viewBox=\"0 0 640 360\"><rect width=\"640\" height=\"360\" fill=\"#e2e8f0\"/><text x=\"320\" y=\"180\" dominant-baseline=\"middle\" text-anchor=\"middle\" fill=\"#94a3b8\" font-size=\"24\" font-family=\"Arial\">{label}</text></svg>";
                return new ContentResult
                {
                    ContentType = "image/svg+xml",
                    Content = svg,
                    StatusCode = 200
                };
            }

            return new FileContentResult(Array.Empty<byte>(), contentType);
        }

        private async Task<IActionResult> SavePageInternalAsync(int projectId, string file, string html, bool publish, bool overrideRisk = false)
        {
            var project = await FindScopedProjectAsync(projectId);
            if (project == null)
            {
                return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
            }

            EnsureProjectExtracted(project, project.FolderName);
            var cleanPath = NormalizeProjectPath(project.FolderName, file);
            if (string.IsNullOrWhiteSpace(cleanPath) || cleanPath.Contains(".."))
            {
                return new JsonResult(new { success = false, message = "Invalid path." }) { StatusCode = 400 };
            }

            var fullPath = ResolveProjectFilePath(project, cleanPath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var sanitized = html ?? string.Empty;
            sanitized = InjectMotionRuntime(sanitized);

            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var projectRoot = Path.Combine(webRoot, "Uploads", project.FolderName);
            var before = System.IO.File.Exists(fullPath)
                ? await System.IO.File.ReadAllTextAsync(fullPath, Encoding.UTF8)
                : string.Empty;
            var preflight = _preflightService.Evaluate(new PreflightPublishRequest
            {
                Project = project,
                FilePath = cleanPath,
                HtmlBefore = before,
                HtmlAfter = sanitized,
                WebRootPath = webRoot,
                ProjectRootPath = projectRoot
            });
            if (!preflight.Safe && !overrideRisk)
            {
                return new JsonResult(new
                {
                    success = false,
                    message = "Preflight blocked this operation.",
                    safe = preflight.Safe,
                    score = preflight.Score,
                    blockers = preflight.Blockers,
                    warnings = preflight.Warnings,
                    changedAbsolute = preflight.ChangedAbsolute,
                    diffSummary = preflight.DiffSummary
                }) { StatusCode = 409 };
            }

            await System.IO.File.WriteAllTextAsync(fullPath, sanitized, Encoding.UTF8);
            DeleteDraftIfExists(project, cleanPath);

            var fileInfo = new FileInfo(fullPath);
            var cleanPathLower = cleanPath.ToLowerInvariant();
            var existing = await _db.UploadedProjectFiles.FirstOrDefaultAsync(f =>
                f.UploadedProjectId == projectId &&
                f.RelativePath.ToLower() == cleanPathLower &&
                !f.IsFolder);
            if (existing == null)
            {
                _db.UploadedProjectFiles.Add(new UploadedProjectFile
                {
                    UploadedProjectId = projectId,
                    RelativePath = cleanPath,
                    SizeBytes = fileInfo.Length,
                    IsFolder = false
                });
            }
            else
            {
                existing.SizeBytes = fileInfo.Length;
            }

            if (publish)
            {
                try
                {
                    await _projectPublishService.PublishAsync(project.Id, "editor", HttpContext.RequestAborted);
                }
                catch
                {
                    // Best-effort publish for editor preview.
                }
            }

            await _db.SaveChangesAsync();
            var currentUser = await _userManager.GetUserAsync(User);
            var snapshot = await _snapshotService.CreateSnapshotAsync(
                project,
                publish ? "live" : "draft",
                publish ? "editor-publish" : "editor-save",
                currentUser?.Id,
                isSuccessful: publish ? true : preflight.Safe,
                versionLabel: publish ? $"live-{DateTime.UtcNow:yyyyMMddHHmmss}" : null,
                cancellationToken: HttpContext.RequestAborted);

            _logger.LogInformation(
                "Editor {Mode} completed for project {ProjectId} safe={Safe} score={Score} blockers={Blockers} warnings={Warnings} snapshot={SnapshotId}",
                publish ? "publish" : "save",
                project.Id,
                preflight.Safe,
                preflight.Score,
                preflight.Blockers.Count,
                preflight.Warnings.Count,
                snapshot.Snapshot?.Id);

            return new JsonResult(new
            {
                success = true,
                published = publish,
                safe = preflight.Safe,
                score = preflight.Score,
                blockers = preflight.Blockers,
                warnings = preflight.Warnings,
                snapshotId = snapshot.Snapshot?.Id
            });
        }

        private static string InjectMotionRuntime(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return html;
            if (!html.Contains("data-bugence-motion", StringComparison.OrdinalIgnoreCase)) return html;
            if (html.Contains("bugence-motion-runtime", StringComparison.OrdinalIgnoreCase)) return html;

            const string runtime = """
<script id="bugence-motion-runtime">
(() => {
  const clamp = (v, min, max) => Math.min(max, Math.max(min, v));
  const lerp = (a, b, t) => a + (b - a) * t;
  const easeValue = (e, t) => {
    if (e === 'ease-in-out') return t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2;
    if (e === 'ease-out') return 1 - Math.pow(1 - t, 3);
    if (e === 'ease') return t * t * (3 - 2 * t);
    return t;
  };
  const parseMotion = (value) => {
    try { return JSON.parse(value); } catch { return null; }
  };
  const applyMotionAt = (el, progress) => {
    const motion = parseMotion(el.getAttribute('data-bugence-motion') || '');
    if (!motion || !Array.isArray(motion.tracks)) return;
    const clamped = clamp(progress, 0, 100);
    const transforms = {};
    motion.tracks.forEach(track => {
      if (!track || !Array.isArray(track.keyframes) || !track.keyframes.length) return;
      const frames = track.keyframes.slice().sort((a, b) => a.t - b.t);
      let prev = frames[0];
      let next = frames[frames.length - 1];
      for (let i = 0; i < frames.length; i += 1) {
        if (clamped <= frames[i].t) { next = frames[i]; prev = frames[i - 1] || frames[i]; break; }
      }
      const span = next.t - prev.t;
      const localT = span === 0 ? 0 : (clamped - prev.t) / span;
      const eased = easeValue(track.easing || 'linear', clamp(localT, 0, 1));
      const value = lerp(Number(prev.value || 0), Number(next.value || 0), eased);
      if (track.property === 'opacity') {
        el.style.opacity = value;
      } else if (track.property === 'translateY') {
        transforms.translateY = value;
      } else if (track.property === 'scale') {
        transforms.scale = value;
      } else if (track.property === 'rotate') {
        transforms.rotate = value;
      } else if (track.property) {
        el.style[track.property] = value;
      }
    });
    const parts = [];
    if (typeof transforms.translateY === 'number') parts.push(`translateY(${transforms.translateY}px)`);
    if (typeof transforms.scale === 'number') parts.push(`scale(${transforms.scale})`);
    if (typeof transforms.rotate === 'number') parts.push(`rotate(${transforms.rotate}deg)`);
    if (parts.length) el.style.transform = parts.join(' ');
  };
  const updateScroll = () => {
    const nodes = Array.from(document.querySelectorAll('[data-bugence-motion]'));
    if (!nodes.length) return;
    const viewHeight = window.innerHeight || document.documentElement.clientHeight || 1;
    nodes.forEach(el => {
      const motion = parseMotion(el.getAttribute('data-bugence-motion') || '');
      if (!motion?.scroll?.enabled) return;
      const rect = el.getBoundingClientRect();
      const start = (motion.scroll.start ?? 10) / 100 * viewHeight;
      const end = (motion.scroll.end ?? 90) / 100 * viewHeight;
      const span = Math.max(1, Math.abs(start - end));
      const raw = start >= end ? (start - rect.top) / span : (rect.top - start) / span;
      const progress = clamp(raw * 100, 0, 100);
      applyMotionAt(el, progress);
    });
  };
  const renderInitial = () => {
    document.querySelectorAll('[data-bugence-motion]').forEach(el => applyMotionAt(el, 0));
    updateScroll();
  };
  window.addEventListener('scroll', () => requestAnimationFrame(updateScroll), { passive: true });
  window.addEventListener('resize', () => requestAnimationFrame(updateScroll));
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', renderInitial);
  } else {
    renderInitial();
  }
})();
</script>
""";

            var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyClose >= 0)
            {
                return html.Insert(bodyClose, runtime);
            }
            var htmlClose = html.LastIndexOf("</html>", StringComparison.OrdinalIgnoreCase);
            if (htmlClose >= 0)
            {
                return html.Insert(htmlClose, runtime);
            }
            return html + runtime;
        }

        public class PageMetaPayload
        {
            public PageMetaItem[] Items { get; set; } = Array.Empty<PageMetaItem>();
        }

        public class PageMetaItem
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public string? Path { get; set; }
            public string? Status { get; set; }
            public bool IsHome { get; set; }
            public long UpdatedAt { get; set; }
        }

        public class PageRenameRequest
        {
            public string? Path { get; set; }
            public string? NewPath { get; set; }
        }

        public class PageDuplicateRequest
        {
            public string? SourcePath { get; set; }
            public string? DestinationPath { get; set; }
        }

        public class PageDeleteRequest
        {
            public string? Path { get; set; }
        }

        public class DeleteAssetRequest
        {
            public string? Path { get; set; }
        }

        public class AiAssistRequest
        {
            public string? Action { get; set; }
            public string? Prompt { get; set; }
            public string? Selection { get; set; }
            public string? Context { get; set; }
        }

        public class PublishStatusRequest
        {
            public bool Published { get; set; }
        }
    }
}
