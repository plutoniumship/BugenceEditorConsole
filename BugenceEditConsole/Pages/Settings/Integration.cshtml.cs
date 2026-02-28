using System.Security.Claims;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Settings;

[Authorize]
public class IntegrationModel : PageModel
{
    private const string TokenProvider = "BugenceIntegrations";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuthenticationSchemeProvider _schemeProvider;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<IntegrationModel> _logger;

    public IntegrationModel(
        UserManager<ApplicationUser> userManager,
        IAuthenticationSchemeProvider schemeProvider,
        ApplicationDbContext db,
        ILogger<IntegrationModel> logger)
    {
        _userManager = userManager;
        _schemeProvider = schemeProvider;
        _db = db;
        _logger = logger;
    }

    public IReadOnlyList<IntegrationCardViewModel> Cards { get; private set; } = Array.Empty<IntegrationCardViewModel>();
    public int ConnectedCount { get; private set; }
    public int TotalCount => Cards.Count;

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? StatusKind { get; set; }

    public async Task<IActionResult> OnGetAsync(string? statusMessage = null, string? statusKind = null)
    {
        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            StatusMessage = statusMessage;
            StatusKind = statusKind;
        }

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return RedirectToPage("/Auth/Login", new { returnUrl = Url.Page("/Settings/Integration") });
        }

        await LoadCardsAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostDisconnectAsync(string provider)
    {
        provider = (provider ?? string.Empty).Trim().ToLowerInvariant();
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return RedirectToPage("/Auth/Login", new { returnUrl = Url.Page("/Settings/Integration") });
        }

        if (string.Equals(provider, "github", StringComparison.OrdinalIgnoreCase))
        {
            await DisconnectProviderAsync(user, "GitHub");
            StatusKind = "success";
            StatusMessage = "GitHub disconnected.";
            return RedirectToPage();
        }

        var providerName = provider switch
        {
            "facebook" => "Facebook",
            "instagram" => "Instagram",
            "linkedin" => "LinkedIn",
            "whatsapp" => "WhatsApp",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(providerName))
        {
            StatusKind = "error";
            StatusMessage = "Unsupported provider.";
            return RedirectToPage();
        }

        await DisconnectProviderAsync(user, providerName);
        StatusKind = "success";
        StatusMessage = $"{providerName} disconnected.";
        return RedirectToPage();
    }

    private async Task LoadCardsAsync(ApplicationUser user)
    {
        var schemes = await _schemeProvider.GetAllSchemesAsync();
        var schemeNames = new HashSet<string>(schemes.Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        var logins = await _userManager.GetLoginsAsync(user);
        var claims = await _userManager.GetClaimsAsync(user);

        var cards = new List<IntegrationCardViewModel>
        {
            new()
            {
                Key = "facebook",
                Name = "Facebook",
                IconClass = "fa-brands fa-facebook-f",
                Accent = "#1877f2",
                ProviderScheme = "Facebook",
                Category = "Social",
                Summary = "Lead capture, page assets, and campaign connection."
            },
            new()
            {
                Key = "instagram",
                Name = "Instagram",
                IconClass = "fa-brands fa-instagram",
                Accent = "#f77737",
                ProviderScheme = "Instagram",
                Category = "Social",
                Summary = "Connect Instagram identity and social publishing data."
            },
            new()
            {
                Key = "linkedin",
                Name = "LinkedIn",
                IconClass = "fa-brands fa-linkedin-in",
                Accent = "#0a66c2",
                ProviderScheme = "LinkedIn",
                Category = "Social",
                Summary = "Professional channel identity and profile sync."
            },
            new()
            {
                Key = "github",
                Name = "GitHub",
                IconClass = "fa-brands fa-github",
                Accent = "#8b5cf6",
                ProviderScheme = "GitHub",
                Category = "Developer",
                Summary = "Repository, deployment, and developer account linking."
            },
            new()
            {
                Key = "whatsapp",
                Name = "WhatsApp",
                IconClass = "fa-brands fa-whatsapp",
                Accent = "#25d366",
                ProviderScheme = "WhatsApp",
                Category = "Messaging",
                Summary = "Business messaging routing and customer touchpoint sync."
            },
            new()
            {
                Key = "google",
                Name = "Google Search Console",
                IconClass = "fa-brands fa-google",
                Accent = "#4285f4",
                ProviderScheme = "Google",
                Category = "Analytics",
                Summary = "SEO property access, indexing insights, and search visibility data."
            },
            new()
            {
                Key = "outlook",
                Name = "Outlook",
                IconClass = "fa-solid fa-envelope",
                Accent = "#2563eb",
                Category = "Messaging",
                Summary = "Inbox and calendar connection for enterprise communication workflows.",
                IsOAuth = false,
                ModeLabel = "Planned",
                ConnectPath = "/Support/PrivacySupport?category=integration_setup&integration=outlook&source=%2FSettings%2FIntegration",
                PrimaryActionLabel = "Request setup"
            },
            new()
            {
                Key = "slack",
                Name = "Slack",
                IconClass = "fa-brands fa-slack",
                Accent = "#4a154b",
                Category = "Operations",
                Summary = "Push deployment alerts, approvals, and workflow notifications to teams.",
                IsOAuth = false,
                ModeLabel = "Planned",
                ConnectPath = "/Settings/GlobalSettings",
                PrimaryActionLabel = "Open config"
            },
            new()
            {
                Key = "hubspot",
                Name = "HubSpot",
                IconClass = "fa-brands fa-hubspot",
                Accent = "#f97316",
                Category = "CRM",
                Summary = "Lead handoff, CRM sync, and campaign-source attribution.",
                IsOAuth = false,
                ModeLabel = "Planned",
                ConnectPath = "/Support/PrivacySupport?category=integration_setup&integration=hubspot&source=%2FSettings%2FIntegration",
                PrimaryActionLabel = "Request setup"
            },
            new()
            {
                Key = "notion",
                Name = "Notion",
                IconClass = "fa-solid fa-book",
                Accent = "#d4d4d8",
                Category = "Workspace",
                Summary = "Mirror operational docs, launch checklists, and content pipelines.",
                IsOAuth = false,
                ModeLabel = "Planned",
                ConnectPath = "/Support/PrivacySupport?category=integration_setup&integration=notion&source=%2FSettings%2FIntegration",
                PrimaryActionLabel = "Request setup"
            },
            new()
            {
                Key = "mailchimp",
                Name = "Mailchimp",
                IconClass = "fa-solid fa-paper-plane",
                Accent = "#facc15",
                Category = "Marketing",
                Summary = "Audience sync, campaign audience segments, and nurture automation.",
                IsOAuth = false,
                ModeLabel = "Planned",
                ConnectPath = "/Support/PrivacySupport?category=integration_setup&integration=mailchimp&source=%2FSettings%2FIntegration",
                PrimaryActionLabel = "Request setup"
            },
            new()
            {
                Key = "stripe",
                Name = "Stripe",
                IconClass = "fa-solid fa-credit-card",
                Accent = "#7c3aed",
                Category = "Payments",
                Summary = "Billing, payment signal routing, and subscription operations.",
                IsOAuth = false,
                ModeLabel = "Configured Elsewhere",
                ConnectPath = "/Settings/Billing",
                PrimaryActionLabel = "Open billing"
            }
        };

        foreach (var card in cards)
        {
            var scheme = card.ProviderScheme;
            card.IsOAuth = card.ProviderScheme.Length > 0;
            card.IsAvailable = !string.IsNullOrWhiteSpace(scheme) && schemeNames.Contains(scheme);
            card.IsConnected = logins.Any(login => string.Equals(login.LoginProvider, scheme, StringComparison.OrdinalIgnoreCase));

            if (card.IsOAuth)
            {
                card.ConnectPath = $"/Auth/ExternalConnect?provider={Uri.EscapeDataString(scheme)}&returnUrl=/Settings/Integration&popup=1";
            }

            var login = logins.FirstOrDefault(x => string.Equals(x.LoginProvider, scheme, StringComparison.OrdinalIgnoreCase));
            card.AccountLabel = ResolveProviderLabel(card.Key, login?.ProviderKey, claims);

            var connectedAtRaw = await _userManager.GetAuthenticationTokenAsync(user, TokenProvider, $"{scheme.ToLowerInvariant()}:connectedAtUtc");
            if (DateTime.TryParse(connectedAtRaw, out var connectedAt))
            {
                card.ConnectedAtUtc = DateTime.SpecifyKind(connectedAt, DateTimeKind.Utc);
            }
        }

        Cards = cards;
        ConnectedCount = cards.Count(x => x.IsConnected);
    }

    private async Task DisconnectProviderAsync(ApplicationUser user, string providerScheme)
    {
        await FacebookLeadTriggerSchemaService.EnsureSchemaAsync(_db);
        var logins = await _userManager.GetLoginsAsync(user);
        foreach (var login in logins.Where(l => string.Equals(l.LoginProvider, providerScheme, StringComparison.OrdinalIgnoreCase)))
        {
            var result = await _userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Failed to remove {Provider} login for user {UserId}.", providerScheme, user.Id);
            }
        }

        var claims = await _userManager.GetClaimsAsync(user);
        var prefix = providerScheme.ToLowerInvariant() + ":";
        foreach (var claim in claims.Where(c => c.Type.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            await _userManager.RemoveClaimAsync(user, claim);
        }

        await _userManager.RemoveAuthenticationTokenAsync(user, TokenProvider, $"{providerScheme.ToLowerInvariant()}:connectedAtUtc");
        await _userManager.RemoveAuthenticationTokenAsync(user, providerScheme, "access_token");
        await _userManager.RemoveAuthenticationTokenAsync(user, providerScheme, "refresh_token");
        await _userManager.RemoveAuthenticationTokenAsync(user, providerScheme, "scope");
        await _userManager.RemoveAuthenticationTokenAsync(user, providerScheme, "expires_at");

        var provider = providerScheme.Trim().ToLowerInvariant();
        var rows = await _db.IntegrationConnections
            .Where(x => x.OwnerUserId == user.Id && x.Provider == provider)
            .ToListAsync();
        foreach (var row in rows)
        {
            row.Status = "disconnected";
            row.UpdatedAtUtc = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }

    private static string? ResolveProviderLabel(string key, string? providerKey, IEnumerable<Claim> claims)
    {
        return key switch
        {
            "facebook" => claims.FirstOrDefault(c => c.Type == "facebook:name")?.Value
                          ?? claims.FirstOrDefault(c => c.Type == "facebook:email")?.Value
                          ?? providerKey,
            "instagram" => claims.FirstOrDefault(c => c.Type == "instagram:username")?.Value ?? providerKey,
            "linkedin" => claims.FirstOrDefault(c => c.Type == "linkedin:name")?.Value
                          ?? claims.FirstOrDefault(c => c.Type == "linkedin:email")?.Value
                          ?? providerKey,
            "github" => claims.FirstOrDefault(c => c.Type == "github:username")?.Value ?? providerKey,
            "whatsapp" => claims.FirstOrDefault(c => c.Type == "whatsapp:name")?.Value ?? providerKey,
            _ => providerKey
        };
    }

    public sealed class IntegrationCardViewModel
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string IconClass { get; set; } = string.Empty;
        public string Accent { get; set; } = "#06b6d4";
        public string Category { get; set; } = "Workspace";
        public string Summary { get; set; } = string.Empty;
        public bool IsOAuth { get; set; }
        public bool IsAvailable { get; set; }
        public bool IsConnected { get; set; }
        public string ProviderScheme { get; set; } = string.Empty;
        public string ConnectPath { get; set; } = string.Empty;
        public string PrimaryActionLabel { get; set; } = "Connect";
        public string ModeLabel { get; set; } = "OAuth";
        public string? AccountLabel { get; set; }
        public DateTime? ConnectedAtUtc { get; set; }
    }
}
