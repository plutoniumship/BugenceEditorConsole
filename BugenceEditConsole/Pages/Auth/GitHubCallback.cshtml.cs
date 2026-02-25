using System.Security.Claims;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BugenceEditConsole.Pages.Auth;

public class GitHubCallbackModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISessionNonceService _sessionNonceService;
    private readonly ILogger<GitHubCallbackModel> _logger;

    public GitHubCallbackModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ISessionNonceService sessionNonceService,
        ILogger<GitHubCallbackModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _sessionNonceService = sessionNonceService;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl ??= Url.Content("~/Dashboard/Index");

        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            _logger.LogWarning("GitHub OAuth error: {Error}", remoteError);
            return RedirectToPage("/Auth/Login", new { returnUrl });
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            _logger.LogWarning("GitHub OAuth info missing.");
            return RedirectToPage("/Auth/Login", new { returnUrl });
        }

        var githubLogin = info.Principal.FindFirstValue("urn:github:login")
                           ?? info.Principal.FindFirstValue(ClaimTypes.Name)
                           ?? "github-user";
        var githubProfile = info.Principal.FindFirstValue("urn:github:url");
        var githubAvatar = info.Principal.FindFirstValue("urn:github:avatar");

        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToPage("/Auth/Login", new { returnUrl });
            }

            var logins = await _userManager.GetLoginsAsync(user);
            if (logins.All(login => login.LoginProvider != info.LoginProvider))
            {
                var addLoginResult = await _userManager.AddLoginAsync(user, info);
                if (!addLoginResult.Succeeded)
                {
                    _logger.LogWarning("Failed to attach GitHub login: {Errors}", string.Join(", ", addLoginResult.Errors.Select(e => e.Description)));
                }
            }

            await UpsertGithubClaimsAsync(user, githubLogin, githubProfile, githubAvatar);
            await _signInManager.RefreshSignInAsync(user);
            return LocalRedirect(returnUrl);
        }

        var signInResult = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider,
            info.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: true);

        if (signInResult.Succeeded)
        {
            var user = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (user != null)
            {
                await UpsertGithubClaimsAsync(user, githubLogin, githubProfile, githubAvatar);
                await _sessionNonceService.RotateAsync(user);
                await _signInManager.RefreshSignInAsync(user);
            }
            return LocalRedirect(returnUrl);
        }

        _logger.LogWarning("GitHub OAuth completed without an active local session.");
        return RedirectToPage("/Auth/Login", new { returnUrl });
    }

    private async Task UpsertGithubClaimsAsync(ApplicationUser user, string? login, string? profileUrl, string? avatarUrl)
    {
        var claims = await _userManager.GetClaimsAsync(user);
        await ReplaceClaimAsync(user, claims, "github:username", login);
        await ReplaceClaimAsync(user, claims, "github:url", profileUrl);
        await ReplaceClaimAsync(user, claims, "github:avatar", avatarUrl);
    }

    private async Task ReplaceClaimAsync(ApplicationUser user, IList<Claim> claims, string type, string? value)
    {
        foreach (var existing in claims.Where(c => c.Type == type).ToList())
        {
            await _userManager.RemoveClaimAsync(user, existing);
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            await _userManager.AddClaimAsync(user, new Claim(type, value));
        }
    }
}
