using System.ComponentModel.DataAnnotations;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BugenceEditConsole.Pages.Support;

public class PrivacySupportModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PrivacySupportModel> _logger;

    public PrivacySupportModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger<PrivacySupportModel> logger)
    {
        _db = db;
        _userManager = userManager;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? Integration { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Source { get; set; }

    [BindProperty]
    public SupportRequestInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? StatusKind { get; set; }

    public string? IntegrationLabel =>
        string.IsNullOrWhiteSpace(Input.IntegrationKey) ? null : FormatIntegrationLabel(Input.IntegrationKey);

    public void OnGet()
    {
        ApplyPrefill();
    }

    public async Task<IActionResult> OnPostSubmitAsync()
    {
        ApplyPrefill();

        if (!ModelState.IsValid)
        {
            StatusKind = "error";
            StatusMessage = "Please complete the required request details.";
            return Page();
        }

        var user = await _userManager.GetUserAsync(User);
        var request = new SupportRequest
        {
            DisplayId = CreateDisplayId(),
            OwnerUserId = user?.Id,
            RequesterName = user is null
                ? User.Identity?.Name
                : string.IsNullOrWhiteSpace(user.GetFriendlyName()) ? user.Email : user.GetFriendlyName(),
            RequesterEmail = user?.Email ?? User?.Claims?.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Email)?.Value,
            Category = NormalizeCategory(Input.Category),
            IntegrationKey = NormalizeKey(Input.IntegrationKey),
            SourcePage = string.IsNullOrWhiteSpace(Input.SourcePage) ? "/Support/PrivacySupport" : Input.SourcePage.Trim(),
            Subject = Input.Subject.Trim(),
            Message = Input.Message.Trim(),
            Status = "submitted",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.SupportRequests.Add(request);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Support request {DisplayId} submitted for category {Category} and integration {Integration}.",
            request.DisplayId,
            request.Category,
            request.IntegrationKey ?? "n/a");

        StatusKind = "success";
        StatusMessage = string.IsNullOrWhiteSpace(request.IntegrationKey)
            ? $"Request submitted. Ticket {request.DisplayId} is now queued."
            : $"Setup request for {FormatIntegrationLabel(request.IntegrationKey)} submitted. Ticket {request.DisplayId} is now queued.";

        return RedirectToPage(new
        {
            integration = request.IntegrationKey,
            category = request.Category,
            source = request.SourcePage
        });
    }

    private void ApplyPrefill()
    {
        Input.Category = NormalizeCategory(Input.Category ?? Category);
        Input.IntegrationKey = NormalizeKey(Input.IntegrationKey ?? Integration);
        Input.SourcePage = string.IsNullOrWhiteSpace(Input.SourcePage) ? (Source ?? "/Settings/Integration") : Input.SourcePage;

        if (string.IsNullOrWhiteSpace(Input.Subject))
        {
            Input.Subject = Input.Category == "integration_setup" && !string.IsNullOrWhiteSpace(Input.IntegrationKey)
                ? $"Setup request: {FormatIntegrationLabel(Input.IntegrationKey)} integration"
                : "Support request";
        }

        if (string.IsNullOrWhiteSpace(Input.Message))
        {
            Input.Message = Input.Category == "integration_setup" && !string.IsNullOrWhiteSpace(Input.IntegrationKey)
                ? $"Please enable and configure the {FormatIntegrationLabel(Input.IntegrationKey)} integration for this workspace."
                : string.Empty;
        }
    }

    private static string NormalizeCategory(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "integration_setup" => "integration_setup",
            "data_export" => "data_export",
            "account_deletion" => "account_deletion",
            "security" => "security",
            _ => "general_support"
        };
    }

    private static string? NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static string CreateDisplayId()
    {
        return $"SUP-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..19].ToUpperInvariant();
    }

    private static string FormatIntegrationLabel(string integrationKey)
    {
        return integrationKey switch
        {
            "hubspot" => "HubSpot",
            "mailchimp" => "Mailchimp",
            "notion" => "Notion",
            "outlook" => "Outlook",
            _ => char.ToUpperInvariant(integrationKey[0]) + integrationKey[1..]
        };
    }

    public sealed class SupportRequestInput
    {
        [Required, MaxLength(64)]
        public string Category { get; set; } = "general_support";

        [MaxLength(64)]
        public string? IntegrationKey { get; set; }

        [MaxLength(128)]
        public string? SourcePage { get; set; }

        [Required, MaxLength(180)]
        public string Subject { get; set; } = string.Empty;

        [Required, MaxLength(4000)]
        public string Message { get; set; } = string.Empty;
    }
}
