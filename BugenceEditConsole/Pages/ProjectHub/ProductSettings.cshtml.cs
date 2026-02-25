using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace BugenceEditConsole.Pages.ProjectHub
{
    public class ProductSettingsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public ProductSettingsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "Project";
        public string? RepoUrl { get; set; }
        public string? Description { get; set; }

        [BindProperty]
        public string DisplayName { get; set; } = string.Empty;

        [BindProperty]
        public string? RepoUrlInput { get; set; }

        [BindProperty]
        public string? DescriptionInput { get; set; }

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
                ProjectName = string.IsNullOrWhiteSpace(project.DisplayName) ? project.FolderName : project.DisplayName;
                DisplayName = ProjectName;
                RepoUrl = project.RepoUrl;
                Description = project.Description;
                RepoUrlInput = RepoUrl;
                DescriptionInput = Description;
            }
        }

        public async Task<IActionResult> OnPostSaveAsync(int projectId)
        {
            var scopedQuery = await GetScopedProjectsQueryAsync();
            var project = await scopedQuery.FirstOrDefaultAsync(p => p.Id == projectId);
            if (project == null)
            {
                return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
            }

            var name = DisplayName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = project.FolderName;
            }

            project.DisplayName = name;
            project.RepoUrl = RepoUrlInput?.Trim();
            project.Description = DescriptionInput?.Trim();
            await _db.SaveChangesAsync();

            return new JsonResult(new { success = true, displayName = project.DisplayName });
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
