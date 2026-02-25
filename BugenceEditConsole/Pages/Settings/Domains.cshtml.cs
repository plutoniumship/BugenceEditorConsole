using BugenceEditConsole.Data;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BugenceEditConsole.Pages.Settings;

public class DomainsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IProjectDomainService _domainService;
    private readonly DomainRoutingOptions _options;
    private readonly UserManager<ApplicationUser> _userManager;

    public DomainsModel(
        ApplicationDbContext db,
        IProjectDomainService domainService,
        IOptions<DomainRoutingOptions> options,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _domainService = domainService;
        _options = options.Value;
        _userManager = userManager;
    }

    public IReadOnlyList<ProjectOption> Projects { get; private set; } = Array.Empty<ProjectOption>();
    public string PrimaryZone => _options.PrimaryZone?.Trim('.') ?? "bugence.app";

    public async Task OnGetAsync(CancellationToken cancellationToken)
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

        var rows = await projectsQuery
            .OrderByDescending(p => p.UploadedAtUtc)
            .Select(p => new
            {
                p.Id,
                Name = string.IsNullOrWhiteSpace(p.DisplayName) ? p.FolderName : p.DisplayName,
                p.Slug
            })
            .ToListAsync(cancellationToken);

        var list = new List<ProjectOption>(rows.Count);
        foreach (var row in rows)
        {
            var primary = await _domainService.EnsurePrimaryDomainAsync(row.Id, cancellationToken);
            list.Add(new ProjectOption
            {
                Id = row.Id,
                Name = row.Name ?? $"Project {row.Id}",
                Slug = row.Slug,
                PrimaryDomain = primary.DomainName
            });
        }

        Projects = list;
    }

    public class ProjectOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string PrimaryDomain { get; set; } = string.Empty;
    }
}
