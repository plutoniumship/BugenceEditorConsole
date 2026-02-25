using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BugenceEditConsole.Pages.ProjectHub
{
    public class CustomCodeModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly UserManager<ApplicationUser> _userManager;

        public CustomCodeModel(ApplicationDbContext db, IWebHostEnvironment env, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _env = env;
            _userManager = userManager;
        }

        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "Project";
        public IReadOnlyList<CodeFileDto> CodeFiles { get; private set; } = Array.Empty<CodeFileDto>();
        public IReadOnlyList<string> FileIndexPaths { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<ExtensionDto> Extensions { get; private set; } = Array.Empty<ExtensionDto>();

        public class CodeFileDto
        {
            public string Path { get; set; } = string.Empty;
            public string Language { get; set; } = "plaintext";
            public string Content { get; set; } = string.Empty;
            public bool ReadOnly { get; set; }
        }

        public class ExtensionDto
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Type { get; set; } = "utility";
            public string FileName { get; set; } = string.Empty;
        }

        public async Task OnGetAsync(int projectId)
        {
            ProjectId = projectId;
            var scopedQuery = await GetScopedProjectsQueryAsync();
            var project = await scopedQuery
                .OrderByDescending(p => p.UploadedAtUtc)
                .FirstOrDefaultAsync(p => p.Id == projectId) ?? await scopedQuery.OrderByDescending(p => p.UploadedAtUtc).FirstOrDefaultAsync();

            if (project != null)
            {
                ProjectId = project.Id;
                ProjectName = project.FolderName;
            }

            if (project != null)
            {
                CodeFiles = LoadProjectFiles(project);
                Extensions = LoadExtensions(ProjectId);
                FileIndexPaths = await LoadFileIndexAsync(ProjectId, ProjectName);
            }
        }

        private IReadOnlyList<CodeFileDto> LoadProjectFiles(UploadedProject project)
        {
            var projectRoot = ResolveProjectRoot(project.FolderName);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return Array.Empty<CodeFileDto>();
            }
            if (!Directory.Exists(projectRoot))
            {
                return Array.Empty<CodeFileDto>();
            }

            var effectiveRoot = ResolveEffectiveProjectRoot(projectRoot);
            var files = Directory.GetFiles(effectiveRoot, "*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderBy(f => f.FullName)
                .ToList();

            var list = new List<CodeFileDto>();
            foreach (var file in files)
            {
                var rel = Path.GetRelativePath(effectiveRoot, file.FullName)
                    .Replace("\\", "/");
                if (string.Equals(Path.GetFileName(rel), ".keep", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var ext = Path.GetExtension(rel).ToLowerInvariant();
                var isText = ext is ".html" or ".htm" or ".css" or ".js" or ".json" or ".md" or ".txt";
                var readOnly = !isText || file.Length > 512 * 1024;
                var content = string.Empty;
                if (!readOnly)
                {
                    try
                    {
                        content = System.IO.File.ReadAllText(file.FullName, Encoding.UTF8);
                    }
                    catch
                    {
                        readOnly = true;
                    }
                }
                if (readOnly && string.IsNullOrWhiteSpace(content))
                {
                    content = $"// {file.Name} is a binary or large file. Open in Media Assets to view.";
                }

                list.Add(new CodeFileDto
                {
                    Path = rel,
                    Language = GetLanguage(ext),
                    Content = content,
                    ReadOnly = readOnly
                });
            }

            return list;
        }

        private void TryExtractProjectPayload(UploadedProject project, string projectRoot)
        {
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
                // Best-effort extraction; fall back to sample files if needed.
            }
        }

        private static string GetLanguage(string ext)
        {
            return ext switch
            {
                ".html" or ".htm" => "html",
                ".css" => "css",
                ".js" => "javascript",
                ".json" => "json",
                ".md" => "markdown",
                _ => "plaintext"
            };
        }

        private string GetUploadsRoot()
        {
            var webRoot = _env.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRoot))
            {
                webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
            }
            var uploadsRoot = Path.Combine(webRoot, "Uploads");
            Directory.CreateDirectory(uploadsRoot);
            return uploadsRoot;
        }

        private async Task<IReadOnlyList<string>> LoadFileIndexAsync(int projectId, string folderName)
        {
            var files = await _db.UploadedProjectFiles
                .Where(f => f.UploadedProjectId == projectId && !f.IsFolder)
                .OrderBy(f => f.RelativePath)
                .Select(f => f.RelativePath)
                .ToListAsync();

            return files
                .Select(path => NormalizeProjectRelativePath(folderName, path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeProjectRelativePath(string folderName, string path)
        {
            var clean = NormalizePath(path);
            if (string.IsNullOrWhiteSpace(clean)) return string.Empty;
            var prefix = NormalizePath(folderName);
            if (!string.IsNullOrWhiteSpace(prefix) &&
                clean.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return clean[(prefix.Length + 1)..];
            }
            return clean;
        }

        private string? ResolveProjectFilePath(string folderName, string cleanPath)
        {
            if (string.IsNullOrWhiteSpace(cleanPath) || cleanPath.Contains(".."))
            {
                return null;
            }

            var projectRoot = ResolveProjectRoot(folderName);
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return null;
            }
            var effectiveRoot = ResolveEffectiveProjectRoot(projectRoot);

            var candidate = cleanPath.Replace("/", Path.DirectorySeparatorChar.ToString());
            var fullPath = Path.Combine(effectiveRoot, candidate);
            if (System.IO.File.Exists(fullPath))
            {
                return fullPath;
            }

            return null;
        }

        public async Task<IActionResult> OnGetFileContentAsync(int projectId, string path)
        {
            var project = await (await GetScopedProjectsQueryAsync()).FirstOrDefaultAsync(p => p.Id == projectId);
            if (project == null)
            {
                return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
            }

            var cleanPath = NormalizeProjectRelativePath(project.FolderName, path);
            var fullPath = ResolveProjectFilePath(project.FolderName, cleanPath);
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return new JsonResult(new { success = false, message = "File not found." }) { StatusCode = 404 };
            }

            var ext = Path.GetExtension(cleanPath).ToLowerInvariant();
            var isText = ext is ".html" or ".htm" or ".css" or ".js" or ".json" or ".md" or ".txt";
            var fileInfo = new FileInfo(fullPath);
            var readOnly = !isText || fileInfo.Length > 512 * 1024;
            string content;

            if (readOnly)
            {
                content = $"// {fileInfo.Name} is a binary or large file. Open in Media Assets to view.";
            }
            else
            {
                content = await System.IO.File.ReadAllTextAsync(fullPath, Encoding.UTF8);
            }

            return new JsonResult(new
            {
                success = true,
                content,
                readOnly,
                language = GetLanguage(ext)
            });
        }

        private string GetExtensionsRoot(int projectId)
        {
            var webRoot = _env.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRoot))
            {
                webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
            }
            var extensionsRoot = Path.Combine(webRoot, "Extensions", projectId.ToString());
            Directory.CreateDirectory(extensionsRoot);
            return extensionsRoot;
        }

        private string GetExtensionsManifestPath(int projectId)
        {
            return Path.Combine(GetExtensionsRoot(projectId), "extensions.json");
        }

        private IReadOnlyList<ExtensionDto> LoadExtensions(int projectId)
        {
            var manifest = GetExtensionsManifestPath(projectId);
            if (!System.IO.File.Exists(manifest))
            {
                return Array.Empty<ExtensionDto>();
            }
            try
            {
                var json = System.IO.File.ReadAllText(manifest, Encoding.UTF8);
                var list = JsonSerializer.Deserialize<List<ExtensionDto>>(json);
                return list ?? new List<ExtensionDto>();
            }
            catch
            {
                return Array.Empty<ExtensionDto>();
            }
        }

        private async Task SaveExtensionsAsync(int projectId, List<ExtensionDto> extensions)
        {
            var manifest = GetExtensionsManifestPath(projectId);
            var json = JsonSerializer.Serialize(extensions);
            await System.IO.File.WriteAllTextAsync(manifest, json, Encoding.UTF8);
        }

        private string? ResolveProjectRoot(string folderName)
        {
            var roots = new[]
            {
                GetUploadsRoot(),
                Path.Combine(_env.ContentRootPath, "Uploads"),
                Path.Combine(_env.ContentRootPath, "wwwroot", "Uploads"),
                Path.Combine(AppContext.BaseDirectory, "Uploads"),
                Path.Combine(AppContext.BaseDirectory, "wwwroot", "Uploads")
            }
            .Select(r => string.IsNullOrWhiteSpace(r) ? string.Empty : Path.GetFullPath(r))
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

            foreach (var root in roots)
            {
                var candidate = Path.Combine(root, folderName);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static string ResolveEffectiveProjectRoot(string projectRoot)
        {
            var topDirs = Directory.GetDirectories(projectRoot);
            var topFiles = Directory.GetFiles(projectRoot);
            if (topDirs.Length == 1 && topFiles.Length == 0)
            {
                return topDirs[0];
            }
            return projectRoot;
        }

        private static string NormalizePath(string path)
        {
            path ??= string.Empty;
            path = path.Replace("\\", "/");
            while (path.StartsWith("/")) path = path[1..];
            return path.Trim();
        }

        public async Task<IActionResult> OnPostSaveFileAsync(int projectId, string path, string content)
        {
            var project = await (await GetScopedProjectsQueryAsync()).FirstOrDefaultAsync(p => p.Id == projectId);
            if (project == null) return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };

            var cleanPath = NormalizeProjectRelativePath(project.FolderName, path);
            if (string.IsNullOrWhiteSpace(cleanPath) || cleanPath.Contains(".."))
            {
                return new JsonResult(new { success = false, message = "Invalid path." }) { StatusCode = 400 };
            }

            var uploadsRoot = GetUploadsRoot();
            var projectRoot = Path.Combine(uploadsRoot, project.FolderName);
            Directory.CreateDirectory(projectRoot);
            var fullPath = Path.Combine(projectRoot, cleanPath.Replace("/", Path.DirectorySeparatorChar.ToString()));
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
            await System.IO.File.WriteAllTextAsync(fullPath, content ?? string.Empty, Encoding.UTF8);

            var cleanPathLower = cleanPath.ToLowerInvariant();
            var existing = await _db.UploadedProjectFiles.FirstOrDefaultAsync(f =>
                f.UploadedProjectId == projectId &&
                f.RelativePath.ToLower() == cleanPathLower &&
                !f.IsFolder);
            var fileInfo = new FileInfo(fullPath);
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

            var segments = cleanPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = string.Empty;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                current = string.IsNullOrEmpty(current) ? segments[i] : $"{current}/{segments[i]}";
                var currentLower = current.ToLowerInvariant();
                var folderExists = await _db.UploadedProjectFiles.AnyAsync(f =>
                    f.UploadedProjectId == projectId &&
                    f.RelativePath.ToLower() == currentLower &&
                    f.IsFolder);
                if (!folderExists)
                {
                    _db.UploadedProjectFiles.Add(new UploadedProjectFile
                    {
                        UploadedProjectId = projectId,
                        RelativePath = current,
                        SizeBytes = 0,
                        IsFolder = true
                    });
                }
            }

            await _db.SaveChangesAsync();
            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostCreateFolderAsync(int projectId, string path)
        {
            var project = await (await GetScopedProjectsQueryAsync()).FirstOrDefaultAsync(p => p.Id == projectId);
            if (project == null) return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };

            var cleanPath = NormalizeProjectRelativePath(project.FolderName, path);
            if (string.IsNullOrWhiteSpace(cleanPath) || cleanPath.Contains(".."))
            {
                return new JsonResult(new { success = false, message = "Invalid path." }) { StatusCode = 400 };
            }

            var uploadsRoot = GetUploadsRoot();
            var projectRoot = Path.Combine(uploadsRoot, project.FolderName);
            var fullPath = Path.Combine(projectRoot, cleanPath.Replace("/", Path.DirectorySeparatorChar.ToString()));
            Directory.CreateDirectory(fullPath);

            var cleanPathLowerFolder = cleanPath.ToLowerInvariant();
            var exists = await _db.UploadedProjectFiles.AnyAsync(f =>
                f.UploadedProjectId == projectId &&
                f.RelativePath.ToLower() == cleanPathLowerFolder &&
                f.IsFolder);
            if (!exists)
            {
                _db.UploadedProjectFiles.Add(new UploadedProjectFile
                {
                    UploadedProjectId = projectId,
                    RelativePath = cleanPath,
                    SizeBytes = 0,
                    IsFolder = true
                });
                await _db.SaveChangesAsync();
            }

            return new JsonResult(new { success = true });
        }

        public async Task<IActionResult> OnPostUploadExtensionAsync(int projectId, IFormFile package, string name, string type)
        {
            var projectExists = await (await GetScopedProjectsQueryAsync()).AnyAsync(p => p.Id == projectId);
            if (!projectExists)
            {
                return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
            }

            if (package == null || package.Length == 0)
            {
                return new JsonResult(new { success = false, message = "No file uploaded." }) { StatusCode = 400 };
            }
            if (package.Length > 20 * 1024 * 1024)
            {
                return new JsonResult(new { success = false, message = "File too large." }) { StatusCode = 400 };
            }

            var safeType = string.IsNullOrWhiteSpace(type) ? "utility" : type.Trim().ToLowerInvariant();
            if (safeType is not ("utility" or "lint" or "formatter" or "theme"))
            {
                safeType = "utility";
            }

            var safeName = string.IsNullOrWhiteSpace(name)
                ? Path.GetFileNameWithoutExtension(package.FileName)
                : name.Trim();

            var extRoot = GetExtensionsRoot(projectId);
            var id = $"ext_{System.DateTime.UtcNow.Ticks}";
            var fileName = $"{id}_{Path.GetFileName(package.FileName)}";
            var fullPath = Path.Combine(extRoot, fileName);

            await using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await package.CopyToAsync(stream);
            }

            var extensions = LoadExtensions(projectId).ToList();
            var entry = new ExtensionDto
            {
                Id = id,
                Name = safeName,
                Type = safeType,
                FileName = fileName
            };
            extensions.Add(entry);
            await SaveExtensionsAsync(projectId, extensions);

            return new JsonResult(new { success = true, extension = entry });
        }

        private async Task<IQueryable<UploadedProject>> GetScopedProjectsQueryAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            var query = _db.UploadedProjects.AsQueryable();
            if (user?.CompanyId != null)
            {
                return query.Where(p => p.CompanyId == user.CompanyId);
            }

            if (!string.IsNullOrWhiteSpace(user?.Id))
            {
                return query.Where(p => p.UserId == user.Id);
            }

            return query.Where(_ => false);
        }
    }
}
