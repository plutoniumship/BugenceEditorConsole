using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BugenceEditConsole.Pages.DeployLogs
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IProjectSnapshotService _snapshotService;
        private readonly IProjectPublishService _projectPublishService;

        public IndexModel(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IProjectSnapshotService snapshotService,
            IProjectPublishService projectPublishService)
        {
            _db = db;
            _userManager = userManager;
            _snapshotService = snapshotService;
            _projectPublishService = projectPublishService;
        }

        public IReadOnlyList<DeployLog> Logs { get; private set; } = new List<DeployLog>();
        public string ProjectName { get; private set; } = "Project";

        public class DeployLog
        {
            public int ProjectId { get; set; }
            public string ProjectName { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public string Hash { get; set; } = string.Empty;
            public string Branch { get; set; } = string.Empty;
            public string UserName { get; set; } = string.Empty;
            public string UserInitials { get; set; } = string.Empty;
            public string Role { get; set; } = "Administrator";
            public string Time { get; set; } = string.Empty;
            public string Status { get; set; } = "Success";
            public string Link { get; set; } = "#";
            public string Duration { get; set; } = "45s";
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
                .OrderByDescending(p => p.UploadedAtUtc)
                .Take(25)
                .ToListAsync();

            if (projects.Any())
            {
                ProjectName = projects.First().FolderName;
            }

            if (projects.Any())
            {
                var userName = User?.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(User.Identity?.Name)
                    ? User.Identity!.Name!
                    : "Administrator";
                var userInitials = new string(userName
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(part => !string.IsNullOrWhiteSpace(part))
                    .Select(part => char.ToUpperInvariant(part[0]))
                    .Take(2)
                    .ToArray());
                if (string.IsNullOrWhiteSpace(userInitials))
                {
                    userInitials = "AD";
                }

                Logs = projects.Select(p => new DeployLog
                {
                    ProjectId = p.Id,
                    ProjectName = p.FolderName,
                    Message = $"Uploaded {p.FolderName}",
                    Hash = "—",
                    Branch = "upload",
                    UserName = userName,
                    UserInitials = userInitials,
                    Role = "Administrator",
                    Time = p.UploadedAtUtc.ToLocalTime().ToString("MMM d, h:mm tt"),
                    Status = NormalizeStatus(p.Status),
                    Duration = "—",
                    Link = $"/DeployLogs/Project?projectId={p.Id}"
                }).ToList();
            }
            else
            {
                Logs = new List<DeployLog>
                {
                    new DeployLog
                    {
                        Message = "No deployments yet",
                        Hash = "",
                        Branch = "",
                        UserName = "System",
                        UserInitials = "SY",
                        Role = "N/A",
                        Time = "",
                        Status = "Failed",
                        Duration = "-",
                        Link = "#"
                    }
                };
            }
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

        public async Task<IActionResult> OnGetDiffAsync(int projectId, long from, long to)
        {
            var user = await _userManager.GetUserAsync(User);
            var hasAccess = await GetScopedProjectsQuery(user?.CompanyId, user?.Id).AnyAsync(p => p.Id == projectId);
            if (!hasAccess)
            {
                return new JsonResult(new { success = false, message = "Unauthorized." }) { StatusCode = 403 };
            }

            var left = await _db.ProjectDeploySnapshots.FirstOrDefaultAsync(s => s.Id == from && s.UploadedProjectId == projectId);
            var right = await _db.ProjectDeploySnapshots.FirstOrDefaultAsync(s => s.Id == to && s.UploadedProjectId == projectId);
            if (left == null || right == null)
            {
                return new JsonResult(new { success = false, message = "Snapshots not found." }) { StatusCode = 404 };
            }

            var leftManifest = _snapshotService.ParseManifest(left.ManifestJson);
            var rightManifest = _snapshotService.ParseManifest(right.ManifestJson);
            var diff = _snapshotService.Diff(leftManifest, rightManifest);
            return new JsonResult(new
            {
                success = true,
                added = diff.Added.Select(x => new { path = x.Path, sizeBytes = x.SizeBytes }),
                removed = diff.Removed.Select(x => new { path = x.Path, sizeBytes = x.SizeBytes }),
                changed = diff.Changed.Select(x => new
                {
                    path = x.To.Path,
                    fromHash = x.From.Sha256,
                    toHash = x.To.Sha256,
                    fromSize = x.From.SizeBytes,
                    toSize = x.To.SizeBytes
                })
            });
        }

        public async Task<IActionResult> OnPostRollbackAsync(int projectId, long snapshotId)
        {
            var user = await _userManager.GetUserAsync(User);
            var project = await GetScopedProjectsQuery(user?.CompanyId, user?.Id).FirstOrDefaultAsync(p => p.Id == projectId);
            if (project == null)
            {
                return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
            }

            var snapshot = await _db.ProjectDeploySnapshots
                .OrderByDescending(s => s.CreatedAtUtc)
                .FirstOrDefaultAsync(s => s.Id == snapshotId && s.UploadedProjectId == projectId);
            if (snapshot == null)
            {
                return new JsonResult(new { success = false, message = "Snapshot not found." }) { StatusCode = 404 };
            }

            var restored = await _snapshotService.RestoreSnapshotAsync(project, snapshot, HttpContext.RequestAborted);
            if (!restored.Success)
            {
                return new JsonResult(new { success = false, message = restored.Message }) { StatusCode = 400 };
            }

            var publishResult = await _projectPublishService.PublishAsync(project.Id, "rollback", HttpContext.RequestAborted);
            var newSnapshot = await _snapshotService.CreateSnapshotAsync(
                project,
                "live",
                "rollback",
                user?.Id,
                isSuccessful: publishResult.Success,
                versionLabel: $"rollback-{snapshot.Id}",
                cancellationToken: HttpContext.RequestAborted);

            return new JsonResult(new
            {
                success = publishResult.Success,
                message = publishResult.Success ? "Rollback complete." : publishResult.Message,
                restoredSnapshotId = snapshot.Id,
                newSnapshotId = newSnapshot.Snapshot?.Id
            });
        }
    }
}
