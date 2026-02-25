using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace BugenceEditConsole.Pages.Auth;

public class GitHubModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IAuthenticationSchemeProvider _schemeProvider;

    public GitHubModel(SignInManager<ApplicationUser> signInManager, IAuthenticationSchemeProvider schemeProvider)
    {
        _signInManager = signInManager;
        _schemeProvider = schemeProvider;
    }

    public IActionResult OnGet(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/Dashboard/Index");
        var scheme = _schemeProvider.GetSchemeAsync("GitHub").GetAwaiter().GetResult();
        if (scheme == null)
        {
            var redirect = QueryHelpers.AddQueryString(returnUrl, "githubError", "not_configured");
            return LocalRedirect(redirect);
        }
        var redirectUrl = Url.Page("/Auth/GitHubCallback", new { returnUrl });
        var props = _signInManager.ConfigureExternalAuthenticationProperties("GitHub", redirectUrl);
        return new ChallengeResult("GitHub", props);
    }
}
