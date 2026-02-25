using BugenceEditConsole.Data;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BugenceEditConsole.Pages.ProjectHub
{
    public class IndexModel : PageModel
    {
        private readonly IWebHostEnvironment _env;
        private readonly ApplicationDbContext _db;
        private readonly DomainRoutingOptions _domainOptions;
        private readonly UserManager<ApplicationUser> _userManager;

        public IndexModel(IWebHostEnvironment env, ApplicationDbContext db, IOptions<DomainRoutingOptions> domainOptions, UserManager<ApplicationUser> userManager)
        {
            _env = env;
            _db = db;
            _domainOptions = domainOptions.Value;
            _userManager = userManager;
        }

        // --- View Models ---
        public class UploadedProjectView
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Status { get; set; }
            public DateTime UploadedAtUtc { get; set; }
            public long SizeBytes { get; set; }
            public string FolderName { get; set; }
            public string Slug { get; set; } = string.Empty;
        }

        public class ProjectFile
        {
            public string FileName { get; set; }
            public string FilePath { get; set; } // Virtual path for links
            public string RelativePath { get; set; } = string.Empty;
            public string Extension { get; set; }
            public DateTime LastModified { get; set; }
            public string Size { get; set; }
            public string Status { get; set; }
        }

        // --- Properties ---
        public List<UploadedProjectView> Projects { get; set; } = new List<UploadedProjectView>();
        public UploadedProjectView Selected { get; set; }
        public List<ProjectFile> ProjectFiles { get; set; } = new List<ProjectFile>();
        public string ResolvedPath { get; set; } = "Scanning..."; // For debugging UI
        public string PrimaryZone { get; private set; } = "bugence.app";

        [BindProperty(SupportsGet = true)]
        public int? ProjectId { get; set; }

        public async Task OnGetAsync()
        {
            PrimaryZone = _domainOptions.PrimaryZone?.Trim('.') ?? "bugence.app";
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

            var dbProjects = await projectsQuery
                .OrderByDescending(p => p.UploadedAtUtc)
                .ToListAsync();

            Projects = dbProjects
                .Select(p => new UploadedProjectView
                {
                    Id = p.Id,
                    Name = string.IsNullOrWhiteSpace(p.DisplayName) ? p.FolderName : p.DisplayName,
                    Slug = p.Slug,
                    FolderName = p.FolderName,
                    Status = string.IsNullOrWhiteSpace(p.Status) ? "Uploaded" : p.Status,
                    UploadedAtUtc = p.UploadedAtUtc,
                    SizeBytes = p.SizeBytes
                })
                .ToList();

            if (Projects.Count == 0) return;

            if (ProjectId.HasValue)
                Selected = Projects.FirstOrDefault(p => p.Id == ProjectId.Value);

            if (Selected == null)
            {
                Selected = Projects.First();
                ProjectId = Selected.Id;
            }

            // Build file listing from DB entries
            var files = await _db.UploadedProjectFiles
                .Where(f => f.UploadedProjectId == Selected.Id && !f.IsFolder)
                .ToListAsync();

            ResolvedPath = $"/Uploads/{Selected.FolderName}";

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file.RelativePath);
                var ext = Path.GetExtension(fileName).ToLowerInvariant();
                var isPage = ext is ".html" or ".htm";

                ProjectFiles.Add(new ProjectFile
                {
                    FileName = fileName,
                    RelativePath = file.RelativePath,
                    Extension = ext.TrimStart('.').ToUpperInvariant(),
                    LastModified = Selected.UploadedAtUtc,
                    Size = FormatSize(file.SizeBytes),
                    Status = isPage ? "Published" : "Asset",
                    FilePath = $"{ResolvedPath}/{file.RelativePath}".Replace("//", "/")
                });
            }

            ProjectFiles = ProjectFiles
                .OrderByDescending(f => f.Status == "Published")
                .ThenBy(f => f.FileName)
                .ToList();
        }

        private string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int counter = 0;
            decimal number = (decimal)bytes;
            while (Math.Round(number / 1024) >= 1) { number = number / 1024; counter++; }
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }

        // AJAX Stubs
        public IActionResult OnGetProjects() { return new JsonResult(Projects); }
        public IActionResult OnPostCreatePage() { return new JsonResult(new { success = true }); }
        public IActionResult OnPostDeletePage([FromBody] dynamic data) { return new JsonResult(new { success = true }); }
    }
}
