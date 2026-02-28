using System.Security.Claims;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace BugenceEditConsole.Pages.Auth;

[Authorize]
public class ExternalConnectModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthenticationSchemeProvider _schemeProvider;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ExternalConnectModel> _logger;

    public ExternalConnectModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuthenticationSchemeProvider schemeProvider,
        ApplicationDbContext db,
        ILogger<ExternalConnectModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _schemeProvider = schemeProvider;
        _db = db;
        _logger = logger;
    }

    public IActionResult OnGet(string provider, string? returnUrl = null, bool popup = false)
    {
        returnUrl ??= Url.Content("~/Settings/Integration");
        provider = (provider ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(provider))
        {
            return popup
                ? PopupResult(provider, success: false, "Missing provider.")
                : LocalRedirect(AddStatus(returnUrl, "Missing provider.", isError: true));
        }

        var scheme = _schemeProvider.GetSchemeAsync(provider).GetAwaiter().GetResult();
        if (scheme == null)
        {
            return popup
                ? PopupResult(provider, success: false, $"{provider} integration is not configured yet.")
                : LocalRedirect(AddStatus(returnUrl, $"{provider} integration is not configured yet.", isError: true));
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return LocalRedirect("/Auth/Login?returnUrl=/Settings/Integration");
        }

        var redirectUrl = Url.Page("/Auth/ExternalConnect", "Callback", new { provider, returnUrl, popup });
        var props = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl, userId);
        return new ChallengeResult(provider, props);
    }

    public async Task<IActionResult> OnGetCallbackAsync(string provider, string? returnUrl = null, string? remoteError = null, bool popup = false)
    {
        returnUrl ??= Url.Content("~/Settings/Integration");
        provider = (provider ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            return popup
                ? PopupResult(provider, success: false, $"External sign-in failed: {remoteError}")
                : LocalRedirect(AddStatus(returnUrl, $"External sign-in failed: {remoteError}", isError: true));
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return LocalRedirect("/Auth/Login?returnUrl=/Settings/Integration");
        }

        var info = await _signInManager.GetExternalLoginInfoAsync(await _userManager.GetUserIdAsync(user));
        if (info == null)
        {
            return popup
                ? PopupResult(provider, success: false, "Unable to load provider information.")
                : LocalRedirect(AddStatus(returnUrl, "Unable to load provider information.", isError: true));
        }

        try
        {
            await FacebookLeadTriggerSchemaService.EnsureSchemaAsync(_db);
            var logins = await _userManager.GetLoginsAsync(user);
            if (logins.All(login => !string.Equals(login.LoginProvider, info.LoginProvider, StringComparison.OrdinalIgnoreCase)))
            {
                var result = await _userManager.AddLoginAsync(user, info);
                if (!result.Succeeded)
                {
                    var message = string.Join("; ", result.Errors.Select(x => x.Description));
                    return popup
                        ? PopupResult(provider, success: false, $"Unable to link {provider}: {message}")
                        : LocalRedirect(AddStatus(returnUrl, $"Unable to link {provider}: {message}", isError: true));
                }
            }

            await UpsertProviderClaimsAsync(user, info.LoginProvider, info.Principal);
            await PersistAuthenticationTokensAsync(user, info);
            await UpsertIntegrationConnectionAsync(user, info);
            await _userManager.SetAuthenticationTokenAsync(
                user,
                "BugenceIntegrations",
                $"{info.LoginProvider.ToLowerInvariant()}:connectedAtUtc",
                DateTime.UtcNow.ToString("O"));
            await _signInManager.RefreshSignInAsync(user);
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return popup
                ? PopupResult(provider, success: true, $"{provider} connected successfully.")
                : LocalRedirect(AddStatus(returnUrl, $"{provider} connected successfully.", isError: false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to attach provider {Provider} for user {UserId}.", provider, user.Id);
            return popup
                ? PopupResult(provider, success: false, $"Unable to connect {provider}.")
                : LocalRedirect(AddStatus(returnUrl, $"Unable to connect {provider}.", isError: true));
        }
    }

    private async Task PersistAuthenticationTokensAsync(ApplicationUser user, ExternalLoginInfo info)
    {
        if (info.AuthenticationTokens == null || !info.AuthenticationTokens.Any())
        {
            return;
        }

        foreach (var token in info.AuthenticationTokens)
        {
            if (string.IsNullOrWhiteSpace(token.Name))
            {
                continue;
            }
            await _userManager.SetAuthenticationTokenAsync(user, info.LoginProvider, token.Name, token.Value ?? string.Empty);
        }
    }

    private async Task UpsertIntegrationConnectionAsync(ApplicationUser user, ExternalLoginInfo info)
    {
        var provider = info.LoginProvider?.Trim().ToLowerInvariant() ?? string.Empty;
        if (provider is not ("facebook" or "instagram" or "linkedin" or "github" or "whatsapp" or "google"))
        {
            return;
        }

        var claims = info.Principal.Claims.ToList();
        var displayName = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value
            ?? claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value
            ?? info.ProviderKey
            ?? info.LoginProvider;
        var externalId = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
            ?? info.ProviderKey
            ?? displayName;

        var scopes = info.AuthenticationTokens?.FirstOrDefault(t => string.Equals(t.Name, "scope", StringComparison.OrdinalIgnoreCase))?.Value;
        var expiresRaw = info.AuthenticationTokens?.FirstOrDefault(t => string.Equals(t.Name, "expires_at", StringComparison.OrdinalIgnoreCase))?.Value;
        DateTime? expiresAtUtc = DateTime.TryParse(expiresRaw, out var expiry) ? DateTime.SpecifyKind(expiry, DateTimeKind.Utc) : null;
        var status = "connected";
        if (expiresAtUtc.HasValue && expiresAtUtc.Value <= DateTime.UtcNow)
        {
            status = "disconnected";
        }
        else if (expiresAtUtc.HasValue && expiresAtUtc.Value <= DateTime.UtcNow.AddDays(7))
        {
            status = "expiring";
        }

        var providerKey = provider == "google" ? "google_search_console" : provider;
        var existing = await _db.IntegrationConnections.FirstOrDefaultAsync(x =>
            x.OwnerUserId == user.Id &&
            x.Provider == providerKey &&
            x.ExternalAccountId == externalId);

        var accessToken = info.AuthenticationTokens?.FirstOrDefault(t => string.Equals(t.Name, "access_token", StringComparison.OrdinalIgnoreCase))?.Value;
        var refreshToken = info.AuthenticationTokens?.FirstOrDefault(t => string.Equals(t.Name, "refresh_token", StringComparison.OrdinalIgnoreCase))?.Value;

        if (existing == null)
        {
            _db.IntegrationConnections.Add(new IntegrationConnection
            {
                Provider = providerKey,
                DisplayName = displayName ?? provider,
                ExternalAccountId = externalId ?? provider,
                Status = status,
                ScopesJson = string.IsNullOrWhiteSpace(scopes)
                    ? "[]"
                    : System.Text.Json.JsonSerializer.Serialize(scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
                AccessTokenEncrypted = accessToken,
                RefreshTokenEncrypted = refreshToken,
                ExpiresAtUtc = expiresAtUtc,
                OwnerUserId = user.Id,
                CompanyId = user.CompanyId,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            existing.DisplayName = displayName ?? existing.DisplayName;
            existing.Status = status;
            existing.ScopesJson = string.IsNullOrWhiteSpace(scopes)
                ? existing.ScopesJson
                : System.Text.Json.JsonSerializer.Serialize(scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            existing.AccessTokenEncrypted = string.IsNullOrWhiteSpace(accessToken) ? existing.AccessTokenEncrypted : accessToken;
            existing.RefreshTokenEncrypted = string.IsNullOrWhiteSpace(refreshToken) ? existing.RefreshTokenEncrypted : refreshToken;
            existing.ExpiresAtUtc = expiresAtUtc ?? existing.ExpiresAtUtc;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    private static string AddStatus(string returnUrl, string message, bool isError)
    {
        var withMessage = QueryHelpers.AddQueryString(returnUrl, "statusMessage", message);
        return QueryHelpers.AddQueryString(withMessage, "statusKind", isError ? "error" : "success");
    }

    private async Task UpsertProviderClaimsAsync(ApplicationUser user, string provider, ClaimsPrincipal principal)
    {
        var claims = await _userManager.GetClaimsAsync(user);
        var providerPrefix = provider.ToLowerInvariant() + ":";
        foreach (var existing in claims.Where(c => c.Type.StartsWith(providerPrefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            await _userManager.RemoveClaimAsync(user, existing);
        }

        string? id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        string? email = principal.FindFirstValue(ClaimTypes.Email);
        string? name = principal.FindFirstValue(ClaimTypes.Name);

        if (!string.IsNullOrWhiteSpace(id))
        {
            await _userManager.AddClaimAsync(user, new Claim($"{providerPrefix}id", id));
        }
        if (!string.IsNullOrWhiteSpace(email))
        {
            await _userManager.AddClaimAsync(user, new Claim($"{providerPrefix}email", email));
        }
        if (!string.IsNullOrWhiteSpace(name))
        {
            await _userManager.AddClaimAsync(user, new Claim($"{providerPrefix}name", name));
        }

        if (string.Equals(provider, "Instagram", StringComparison.OrdinalIgnoreCase))
        {
            var username = principal.FindFirstValue("instagram:username") ?? name;
            if (!string.IsNullOrWhiteSpace(username))
            {
                await _userManager.AddClaimAsync(user, new Claim("instagram:username", username));
            }
        }
    }

    private ContentResult PopupResult(string provider, bool success, string message)
    {
        var safeProvider = System.Net.WebUtility.HtmlEncode(provider ?? string.Empty);
        var safeMessage = System.Net.WebUtility.HtmlEncode(message ?? string.Empty);
        var status = success ? "success" : "error";
        var script = $@"
<!doctype html>
<html>
<head><meta charset=""utf-8""><title>Integration</title></head>
<body style=""font-family:system-ui;background:#0b1020;color:#e2e8f0;display:grid;place-items:center;height:100vh;margin:0;"">
  <div style=""font-size:14px;opacity:.9;"">Finalizing {safeProvider} connection...</div>
  <script>
    (function() {{
      var payload = {{
        type: 'bugence.integration.result',
        provider: '{safeProvider}',
        status: '{status}',
        message: '{safeMessage}'
      }};
      try {{
        if (window.opener && !window.opener.closed) {{
          window.opener.postMessage(payload, window.location.origin);
        }}
      }} catch (e) {{}}
      setTimeout(function() {{ window.close(); }}, 120);
      setTimeout(function() {{
        document.body.innerHTML = '<div style=""font-size:13px;opacity:.9;padding:18px;"">You can close this window.</div>';
      }}, 700);
    }})();
  </script>
</body>
</html>";
        return Content(script, "text/html; charset=utf-8");
    }
}
