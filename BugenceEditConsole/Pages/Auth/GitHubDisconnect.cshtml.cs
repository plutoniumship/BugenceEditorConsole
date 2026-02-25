using System.Security.Claims;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BugenceEditConsole.Pages.Auth;

[Authorize]
public class GitHubDisconnectModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<GitHubDisconnectModel> _logger;

    public GitHubDisconnectModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<GitHubDisconnectModel> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/Dashboard/Index");
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return LocalRedirect(returnUrl);
        }

        var logins = await _userManager.GetLoginsAsync(user);
        foreach (var login in logins.Where(l => l.LoginProvider == "GitHub"))
        {
            var result = await _userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Failed to remove GitHub login for user {UserId}.", user.Id);
            }
        }

        var claims = await _userManager.GetClaimsAsync(user);
        foreach (var claim in claims.Where(c => c.Type.StartsWith("github:", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            await _userManager.RemoveClaimAsync(user, claim);
        }

        await _signInManager.RefreshSignInAsync(user);
        return LocalRedirect(returnUrl);
    }
}
