using BugenceEditConsole.Data;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.ProjectHub;

public class ProductSettingsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public ProductSettingsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public sealed class ProjectPageRouteRow
    {
        public string SourcePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string SuggestedRoute { get; set; } = string.Empty;
        public string RoutePath { get; set; } = string.Empty;
        public bool IsLanding { get; set; }
    }

    public sealed class DomainRow
    {
        public string DomainName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string SslStatus { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public bool IsConnected { get; set; }
    }

    public int ProjectId { get; private set; }
    public string ProjectName { get; private set; } = "Project";
    public string ProjectStatus { get; private set; } = "Uploaded";
    public string PublishRoot { get; private set; } = "Not published";
    public string LastPublishedLabel { get; private set; } = "Not published yet";
    public int HtmlPageCount { get; private set; }
    public int RouteOverrideCount { get; private set; }
    public bool SupportsCustomRoutes { get; private set; }
    public bool HasVisualEditing { get; private set; }
    public IReadOnlyList<ProjectPageRouteRow> HtmlPages { get; private set; } = Array.Empty<ProjectPageRouteRow>();
    public IReadOnlyList<DomainRow> Domains { get; private set; } = Array.Empty<DomainRow>();

    [BindProperty]
    public string DisplayName { get; set; } = string.Empty;

    [BindProperty]
    public string? RepoUrlInput { get; set; }

    [BindProperty]
    public string? DescriptionInput { get; set; }

    [BindProperty]
    public string? LandingPagePath { get; set; }

    [BindProperty]
    public bool AutoDeployOnPush { get; set; }

    [BindProperty]
    public bool EnablePreviewDeploys { get; set; }

    [BindProperty]
    public bool EnforceHttps { get; set; }

    [BindProperty]
    public bool EnableContentSecurityPolicy { get; set; }

    [BindProperty]
    public List<ProjectPageRouteInput> PageRoutes { get; set; } = new();

    public sealed class ProjectPageRouteInput
    {
        public string SourcePath { get; set; } = string.Empty;
        public string? RoutePath { get; set; }
    }

    public async Task OnGetAsync(int projectId)
    {
        var project = await LoadProjectAsync(projectId);
        if (project == null)
        {
            return;
        }

        ApplyProject(project);
    }

    public async Task<IActionResult> OnPostSaveAsync(int projectId)
    {
        var project = await LoadProjectAsync(projectId);
        if (project == null)
        {
            return new JsonResult(new { success = false, message = "Project not found." }) { StatusCode = 404 };
        }

        var htmlFiles = project.Files
            .Where(file => !file.IsFolder)
            .Select(file => NormalizeProjectRelativePath(project.FolderName, file.RelativePath))
            .Where(ProjectRoutingSettings.IsHtmlPage)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var name = DisplayName?.Trim();
        project.DisplayName = string.IsNullOrWhiteSpace(name) ? project.FolderName : name;
        project.RepoUrl = string.IsNullOrWhiteSpace(RepoUrlInput) ? null : RepoUrlInput.Trim();
        project.Description = string.IsNullOrWhiteSpace(DescriptionInput) ? null : DescriptionInput.Trim();
        project.AutoDeployOnPush = AutoDeployOnPush;
        project.EnablePreviewDeploys = EnablePreviewDeploys;
        project.EnforceHttps = EnforceHttps;
        project.EnableContentSecurityPolicy = EnableContentSecurityPolicy;

        var requestedLanding = ProjectRoutingSettings.NormalizeFilePath(LandingPagePath);
        project.LocalPreviewPath = htmlFiles.Contains(requestedLanding, StringComparer.OrdinalIgnoreCase)
            ? requestedLanding
            : htmlFiles.FirstOrDefault(path => Path.GetFileName(path).Equals("index.html", StringComparison.OrdinalIgnoreCase))
                ?? htmlFiles.FirstOrDefault();

        var normalizedAliases = PageRoutes
            .Select(route => new ProjectPageRouteAlias
            {
                SourcePath = NormalizeProjectRelativePath(project.FolderName, route.SourcePath),
                RoutePath = route.RoutePath ?? string.Empty
            })
            .Where(alias => htmlFiles.Contains(alias.SourcePath, StringComparer.OrdinalIgnoreCase))
            .Select(alias => new ProjectPageRouteAlias
            {
                SourcePath = alias.SourcePath,
                RoutePath = ProjectRoutingSettings.NormalizeRoutePath(alias.RoutePath)
            })
            .Where(alias => !string.IsNullOrWhiteSpace(alias.RoutePath))
            .GroupBy(alias => alias.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var duplicateRoute = normalizedAliases
            .GroupBy(alias => alias.RoutePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateRoute != null)
        {
            return new JsonResult(new { success = false, message = $"Route '{duplicateRoute.Key}' is assigned to more than one page." }) { StatusCode = 400 };
        }

        project.PageRouteOverridesJson = ProjectRoutingSettings.SerializeAliases(normalizedAliases);
        await _db.SaveChangesAsync();

        return new JsonResult(new
        {
            success = true,
            displayName = project.DisplayName,
            landingPagePath = project.LocalPreviewPath,
            routeOverrideCount = normalizedAliases.Count
        });
    }

    private async Task<UploadedProject?> LoadProjectAsync(int projectId)
    {
        var scopedQuery = await GetScopedProjectsQueryAsync();
        var project = await scopedQuery
            .Include(project => project.Domains)
            .Include(project => project.Files)
            .Include(project => project.DynamicVeConfig)
            .Include(project => project.DynamicVeRevisions)
            .OrderByDescending(project => project.UploadedAtUtc)
            .FirstOrDefaultAsync(project => project.Id == projectId);

        if (project == null)
        {
            project = await scopedQuery
                .Include(p => p.Domains)
                .Include(p => p.Files)
                .Include(p => p.DynamicVeConfig)
                .Include(p => p.DynamicVeRevisions)
                .OrderByDescending(project => project.UploadedAtUtc)
                .FirstOrDefaultAsync();
        }

        return project;
    }

    private void ApplyProject(UploadedProject project)
    {
        ProjectId = project.Id;
        ProjectName = string.IsNullOrWhiteSpace(project.DisplayName) ? project.FolderName : project.DisplayName!;
        ProjectStatus = string.IsNullOrWhiteSpace(project.Status) ? "Uploaded" : project.Status;
        PublishRoot = string.IsNullOrWhiteSpace(project.PublishStoragePath) ? "Not published" : project.PublishStoragePath!;
        LastPublishedLabel = project.LastPublishedAtUtc?.ToLocalTime().ToString("MMM d, yyyy h:mm tt") ?? "Not published yet";

        DisplayName = ProjectName;
        RepoUrlInput = project.RepoUrl;
        DescriptionInput = project.Description;
        LandingPagePath = ProjectRoutingSettings.NormalizeFilePath(project.LocalPreviewPath);
        AutoDeployOnPush = project.AutoDeployOnPush;
        EnablePreviewDeploys = project.EnablePreviewDeploys;
        EnforceHttps = project.EnforceHttps;
        EnableContentSecurityPolicy = project.EnableContentSecurityPolicy;

        var aliases = ProjectRoutingSettings.ParseAliases(project.PageRouteOverridesJson);
        var htmlPages = project.Files
            .Where(file => !file.IsFolder)
            .Select(file => NormalizeProjectRelativePath(project.FolderName, file.RelativePath))
            .Where(ProjectRoutingSettings.IsHtmlPage)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Contains("index", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        HtmlPageCount = htmlPages.Count;
        HasVisualEditing = htmlPages.Count > 0;
        SupportsCustomRoutes = HasVisualEditing;
        RouteOverrideCount = aliases.Count;

        HtmlPages = htmlPages.Select(path => new ProjectPageRouteRow
        {
            SourcePath = path,
            FileName = Path.GetFileName(path),
            SuggestedRoute = BuildSuggestedRoute(path),
            RoutePath = aliases.TryGetValue(path, out var alias) ? alias : string.Empty,
            IsLanding = string.Equals(path, LandingPagePath, StringComparison.OrdinalIgnoreCase)
        }).ToList();

        PageRoutes = HtmlPages
            .Select(page => new ProjectPageRouteInput
            {
                SourcePath = page.SourcePath,
                RoutePath = page.RoutePath
            })
            .ToList();

        Domains = project.Domains
            .OrderByDescending(domain => domain.DomainType == ProjectDomainType.Primary)
            .ThenBy(domain => domain.DomainName, StringComparer.OrdinalIgnoreCase)
            .Select(domain => new DomainRow
            {
                DomainName = domain.DomainName,
                Status = domain.Status.ToString(),
                SslStatus = domain.SslStatus.ToString(),
                IsPrimary = domain.DomainType == ProjectDomainType.Primary,
                IsConnected = domain.Status == DomainStatus.Connected
            })
            .ToList();
    }

    private async Task<IQueryable<UploadedProject>> GetScopedProjectsQueryAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        var query = _db.UploadedProjects.AsQueryable();

        if (user?.CompanyId != null)
        {
            return query.Where(project => project.CompanyId == user.CompanyId);
        }

        if (!string.IsNullOrWhiteSpace(user?.Id))
        {
            return query.Where(project => project.UserId == user.Id);
        }

        return query.Where(_ => false);
    }

    private static string NormalizeProjectRelativePath(string folderName, string rawPath)
    {
        var normalized = ProjectRoutingSettings.NormalizeFilePath(rawPath);
        var prefix = ProjectRoutingSettings.NormalizeFilePath(folderName);
        if (!string.IsNullOrWhiteSpace(prefix) && normalized.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized[(prefix.Length + 1)..];
        }

        return normalized;
    }

    private static string BuildSuggestedRoute(string path)
    {
        var normalized = ProjectRoutingSettings.NormalizeFilePath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (normalized.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^5];
        }
        else if (normalized.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        if (string.Equals(normalized, "index", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return normalized;
    }
}
