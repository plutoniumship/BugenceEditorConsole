using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

// NAMESPACE MUST MATCH EXACTLY
namespace BugenceEditConsole.Pages.ProjectHub
{
    [IgnoreAntiforgeryToken]
    public class MediaAssetsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly UserManager<ApplicationUser> _userManager;

        public MediaAssetsModel(ApplicationDbContext db, IWebHostEnvironment env, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _env = env;
            _userManager = userManager;
        }

        // Models
        public class Project
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string StorageUsed { get; set; }
            public int StoragePercentage { get; set; }
        }

        public class MediaAsset
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Url { get; set; }
            public string Dimensions { get; set; }
            public string Size { get; set; }
            public long SizeBytes { get; set; }
            public string Type { get; set; }
            public string Extension { get; set; }
            public string UploadDate { get; set; }
        }

        public Project CurrentProject { get; set; }
        public List<MediaAsset> Assets { get; set; } = new List<MediaAsset>();

        [BindProperty(SupportsGet = true)]
        public int ProjectId { get; set; }

        private static readonly HashSet<string> ImageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
        private static readonly HashSet<string> VideoExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".mkv", ".webm" };
        private static readonly HashSet<string> DocExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx", ".txt" };
        private static readonly HashSet<string> PageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".html", ".htm" };

        public async Task OnGetAsync(int projectId)
        {
            ProjectId = projectId;

            var projects = await GetScopedProjectsQuery()
                .Include(p => p.Files)
                .OrderByDescending(p => p.UploadedAtUtc)
                .ToListAsync();

            var selected = projects.FirstOrDefault(p => p.Id == projectId) ?? projects.FirstOrDefault();
            if (selected == null)
            {
                CurrentProject = new Project { Id = 0, Name = "No Projects", StorageUsed = "0 / 0", StoragePercentage = 0 };
                Assets = new List<MediaAsset>();
                return;
            }

            ProjectId = selected.Id;
            var storageCap = 10L * 1024 * 1024 * 1024; // 10 GB cap for display

            CurrentProject = new Project
            {
                Id = selected.Id,
                Name = selected.FolderName,
                StorageUsed = $"{FormatSize(selected.SizeBytes)} / {FormatSize(storageCap)}",
                StoragePercentage = (int)Math.Min(100, Math.Round(selected.SizeBytes * 100.0 / storageCap))
            };

            var basePath = $"/Uploads/{selected.FolderName}";
            var files = selected.Files.Where(f => !f.IsFolder).ToList();

            Assets = files
                .Where(f => !PageExts.Contains(Path.GetExtension(f.RelativePath)))
                .Select(f =>
                {
                    var ext = Path.GetExtension(f.RelativePath);
                    var lower = ext.ToLowerInvariant();
                    var type = ImageExts.Contains(lower) ? "image" : VideoExts.Contains(lower) ? "video" : DocExts.Contains(lower) ? "doc" : "file";
                    var name = Path.GetFileName(f.RelativePath);
                    var safeRel = string.Join("/", f.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
                    var url = $"{basePath}/{safeRel}".Replace("//", "/");

                    var physicalPath = Path.Combine(_env.WebRootPath ?? string.Empty, "Uploads", selected.FolderName, f.RelativePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
                    var exists = System.IO.File.Exists(physicalPath);
                    if (!exists) url = string.Empty;

                    return new MediaAsset
                    {
                        Id = f.Id.ToString(),
                        Name = name,
                        Url = url,
                        Dimensions = "-",
                        Size = FormatSize(f.SizeBytes),
                        SizeBytes = f.SizeBytes,
                        Type = type,
                        Extension = ext.TrimStart('.').ToUpperInvariant(),
                        UploadDate = selected.UploadedAtUtc.ToLocalTime().ToString("g")
                    };
                })
                .OrderByDescending(a => a.Type == "image")
                .ThenBy(a => a.Name)
                .ToList();
        }

        public async Task<IActionResult> OnPostUploadAssetAsync(int projectId, List<IFormFile> upload)
        {
            var project = await GetScopedProjectsQuery().FirstOrDefaultAsync(p => p.Id == projectId);
            if (project == null || upload is null || upload.Count == 0)
                return new JsonResult(new { success = false }) { StatusCode = 400 };

            foreach (var file in upload)
            {
                var path = NormalizePath(file.FileName);
                if (string.IsNullOrWhiteSpace(path)) continue;

                _db.UploadedProjectFiles.Add(new UploadedProjectFile
                {
                    UploadedProjectId = project.Id,
                    RelativePath = path,
                    SizeBytes = file.Length,
                    IsFolder = false
                });
            }

            await _db.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostRenameAssetAsync([FromBody] RenameRequest req)
        {
            if (!int.TryParse(req.AssetId, out var id)) return new JsonResult(new { success = false }) { StatusCode = 400 };
            var projectAllowed = await GetScopedProjectsQuery().AnyAsync(p => p.Id == req.ProjectId);
            if (!projectAllowed) return new JsonResult(new { success = false }) { StatusCode = 404 };
            var asset = await _db.UploadedProjectFiles.FirstOrDefaultAsync(a => a.Id == id && a.UploadedProjectId == req.ProjectId);
            if (asset == null) return new JsonResult(new { success = false }) { StatusCode = 404 };

            var dir = Path.GetDirectoryName(asset.RelativePath)?.Replace("\\", "/");
            var ext = Path.GetExtension(asset.RelativePath);
            var newPath = string.IsNullOrWhiteSpace(dir)
                ? $"{req.NewName}{ext}"
                : $"{dir}/{req.NewName}{ext}";

            asset.RelativePath = NormalizePath(newPath);
            await _db.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostDeleteAssetAsync([FromBody] DeleteRequest req)
        {
            if (!int.TryParse(req.AssetId, out var id)) return new JsonResult(new { success = false }) { StatusCode = 400 };
            var projectAllowed = await GetScopedProjectsQuery().AnyAsync(p => p.Id == req.ProjectId);
            if (!projectAllowed) return new JsonResult(new { success = false }) { StatusCode = 404 };
            var asset = await _db.UploadedProjectFiles.FirstOrDefaultAsync(a => a.Id == id && a.UploadedProjectId == req.ProjectId);
            if (asset != null)
            {
                _db.UploadedProjectFiles.Remove(asset);
                await _db.SaveChangesAsync();
            }
            return new JsonResult(new { success = true });
        }

        public class RenameRequest { public int ProjectId { get; set; } public string AssetId { get; set; } public string NewName { get; set; } }
        public class DeleteRequest { public int ProjectId { get; set; } public string AssetId { get; set; } }

        private static string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
            {
                number = number / 1024;
                counter++;
            }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }

        private static string NormalizePath(string path)
        {
            path ??= string.Empty;
            path = path.Replace("\\", "/");
            while (path.StartsWith("/")) path = path[1..];
            return path.Trim();
        }

        private IQueryable<UploadedProject> GetScopedProjectsQuery()
        {
            var userId = _userManager.GetUserId(User);
            var query = _db.UploadedProjects.AsQueryable();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return query.Where(_ => false);
            }

            var companyId = _db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.CompanyId)
                .FirstOrDefault();

            if (companyId.HasValue)
            {
                return query.Where(p => p.CompanyId == companyId.Value);
            }

            return query.Where(p => p.UserId == userId);
        }
    }
}
