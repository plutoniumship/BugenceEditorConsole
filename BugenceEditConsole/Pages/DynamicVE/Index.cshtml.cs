using BugenceEditConsole.Data;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BugenceEditConsole.Pages.DynamicVE;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly FeatureFlagOptions _featureFlags;

    public IndexModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IOptions<FeatureFlagOptions> featureFlags)
    {
        _db = db;
        _userManager = userManager;
        _featureFlags = featureFlags.Value;
    }

    [BindProperty(SupportsGet = true)]
    public int? ProjectId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? File { get; set; }

    public string PreviewUrl { get; private set; } = "#";
    public string ProjectName { get; private set; } = "Project";
    public IReadOnlyList<ProjectOption> Projects { get; private set; } = Array.Empty<ProjectOption>();
    public IReadOnlyList<string> Files { get; private set; } = Array.Empty<string>();

    public sealed class ProjectOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
    }

    public async Task OnGetAsync()
    {
        if (!_featureFlags.DynamicVeV1)
        {
            Response.Redirect("/Dashboard/Index");
            return;
        }

        var user = await _userManager.GetUserAsync(User);
        var query = _db.UploadedProjects.AsNoTracking().AsQueryable();
        if (user?.CompanyId.HasValue == true)
        {
            query = query.Where(p => p.CompanyId == user.CompanyId);
        }
        else if (!string.IsNullOrWhiteSpace(user?.Id))
        {
            query = query.Where(p => p.UserId == user.Id);
        }
        else
        {
            query = query.Where(_ => false);
        }

        var list = await query
            .OrderByDescending(p => p.UploadedAtUtc)
            .Select(p => new ProjectOption
            {
                Id = p.Id,
                Name = string.IsNullOrWhiteSpace(p.DisplayName) ? p.FolderName : p.DisplayName!,
                FolderName = p.FolderName
            })
            .ToListAsync();
        Projects = list;

        if (!ProjectId.HasValue)
        {
            ProjectId = Projects.FirstOrDefault()?.Id;
        }

        var selected = Projects.FirstOrDefault(p => p.Id == ProjectId);
        if (selected == null)
        {
            return;
        }

        ProjectName = selected.Name;
        var files = await _db.UploadedProjectFiles
            .AsNoTracking()
            .Where(f => f.UploadedProjectId == selected.Id && !f.IsFolder)
            .Select(f => f.RelativePath.Replace("\\", "/"))
            .ToListAsync();
        Files = files
            .Where(p => p.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Contains("index", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p)
            .ToList();

        var chosen = Files.FirstOrDefault(p => p.Equals(File ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            ?? Files.FirstOrDefault()
            ?? "index.html";

        File = chosen;
        PreviewUrl = $"/Uploads/{Uri.EscapeDataString(selected.FolderName)}/{string.Join("/", chosen.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString))}";
    }
}
