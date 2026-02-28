using BugenceEditConsole.Data;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using BugenceEditConsole.Services;

namespace BugenceEditConsole.Pages.Social.Facebook;

[Authorize]
public sealed class SettingsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IOptions<FeatureFlagOptions> _featureFlags;

    public SettingsModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IOptions<FeatureFlagOptions> featureFlags)
    {
        _db = db;
        _userManager = userManager;
        _featureFlags = featureFlags;
    }

    public bool Enabled => _featureFlags.Value.SocialMarketingV1 && _featureFlags.Value.FacebookMarketingV1;
    public IReadOnlyList<IntegrationConnection> Connections { get; private set; } = [];
    public IReadOnlyList<Workflow> Workflows { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return;
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return;
        }

        await SocialMarketingSchemaService.EnsureSchemaAsync(_db);
        Connections = await _db.IntegrationConnections
            .AsNoTracking()
            .Where(x => x.OwnerUserId == user.Id && x.Provider == "facebook")
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        Workflows = await _db.Workflows
            .AsNoTracking()
            .Where(x => x.OwnerUserId == user.Id && x.Status != "Archived" && x.Status != "Deleted")
            .OrderBy(x => x.Name)
            .Take(200)
            .ToListAsync(cancellationToken);
    }
}

