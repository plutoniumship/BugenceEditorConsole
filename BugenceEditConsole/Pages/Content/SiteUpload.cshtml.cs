using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BugenceEditConsole.Pages.Content;

[Authorize]
[RequestSizeLimit(262144000)] // 250 MB + headroom
public class SiteUploadModel : PageModel
{
    private readonly IStaticSiteManager _staticSiteManager;
    private readonly ILogger<SiteUploadModel> _logger;

    public SiteUploadModel(IStaticSiteManager staticSiteManager, ILogger<SiteUploadModel> logger)
    {
        _staticSiteManager = staticSiteManager;
        _logger = logger;
    }

    [BindProperty]
    public IFormFile? Archive { get; set; }

    public StaticSiteState? ActiveSite { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
        ActiveSite = _staticSiteManager.GetActiveState();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ActiveSite = _staticSiteManager.GetActiveState();

        if (Archive is null)
        {
            ModelState.AddModelError(nameof(Archive), "Please choose a .zip archive to upload.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            ActiveSite = await _staticSiteManager.UploadAsync(Archive!);
            StatusMessage = "Upload complete. Active site refreshed and will be served at /.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload static site bundle.");
            ErrorMessage = ex.Message;
        }

        return Page();
    }
}
