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
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IOptions<FeatureFlagOptions> _featureFlags;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IOptions<FeatureFlagOptions> featureFlags,
        ILogger<IndexModel> logger)
    {
        _db = db;
        _userManager = userManager;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    public bool Enabled => _featureFlags.Value.SocialMarketingV1 && _featureFlags.Value.FacebookMarketingV1;
    public IReadOnlyList<IntegrationConnection> Connections { get; private set; } = [];
    public string? LoadError { get; private set; }

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

        try
        {
            await FacebookLeadTriggerSchemaService.EnsureSchemaAsync(_db);
            await SocialMarketingSchemaService.EnsureSchemaAsync(_db);
            Connections = await _db.IntegrationConnections
                .AsNoTracking()
                .Where(x => x.OwnerUserId == user.Id && x.Provider == "facebook")
                .OrderByDescending(x => x.UpdatedAtUtc)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Facebook marketing page for user {UserId}.", user.Id);
            LoadError = "Facebook Marketing is temporarily unavailable. Open Settings > Integration and reconnect Facebook, then refresh this page.";
            Connections = [];
        }
    }
}

