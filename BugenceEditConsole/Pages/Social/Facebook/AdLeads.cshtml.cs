using BugenceEditConsole.Data;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using BugenceEditConsole.Services;

namespace BugenceEditConsole.Pages.Social.Facebook;

[Authorize]
public sealed class AdLeadsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IOptions<FeatureFlagOptions> _featureFlags;

    public AdLeadsModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IOptions<FeatureFlagOptions> featureFlags)
    {
        _db = db;
        _userManager = userManager;
        _featureFlags = featureFlags;
    }

    [FromRoute] public string AdId { get; set; } = string.Empty;
    [FromQuery] public Guid IntegrationConnectionId { get; set; }
    public bool Enabled => _featureFlags.Value.SocialMarketingV1 && _featureFlags.Value.FacebookMarketingV1;
    public MktMetaAd? Ad { get; private set; }
    public IReadOnlyList<Workflow> Workflows { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return Page();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        await SocialMarketingSchemaService.EnsureSchemaAsync(_db);
        var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
        Ad = await _db.MktMetaAds.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenantId &&
            x.WorkspaceId == workspaceId &&
            x.IntegrationConnectionId == IntegrationConnectionId &&
            x.AdId == AdId, cancellationToken);
        Workflows = await _db.Workflows.AsNoTracking()
            .Where(x => x.OwnerUserId == user.Id && x.Status != "Archived" && x.Status != "Deleted")
            .OrderBy(x => x.Name)
            .Take(100)
            .ToListAsync(cancellationToken);

        return Page();
    }
}

