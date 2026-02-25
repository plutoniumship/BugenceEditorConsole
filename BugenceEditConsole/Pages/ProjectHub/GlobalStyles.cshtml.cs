using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BugenceEditConsole.Pages.ProjectHub
{
    public class GlobalStylesModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public GlobalStylesModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "Project";
        public List<StyleColor> DefaultPalette { get; set; } = new();

        public class StyleColor
        {
            public string Name { get; set; } = string.Empty;
            public string Hex { get; set; } = string.Empty;
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

            DefaultPalette = new List<StyleColor>
            {
                new StyleColor { Name = "Primary", Hex = "#06B6D4" },
                new StyleColor { Name = "Secondary", Hex = "#2563EB" },
                new StyleColor { Name = "Success", Hex = "#10B981" },
                new StyleColor { Name = "Highlight", Hex = "#7C3AED" }
            };
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
