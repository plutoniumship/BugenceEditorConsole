using System.ComponentModel.DataAnnotations;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BugenceEditConsole.Pages.Auth;

public class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _emailSender;
    private readonly ISensitiveDataProtector _protector;
    private readonly ISubscriptionService _subscriptionService;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IConfiguration _configuration;

    public RegisterModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ApplicationDbContext db,
        IEmailSender emailSender,
        ISensitiveDataProtector protector,
        ISubscriptionService subscriptionService,
        IPaymentGateway paymentGateway,
        IConfiguration configuration)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _emailSender = emailSender;
        _protector = protector;
        _subscriptionService = subscriptionService;
        _paymentGateway = paymentGateway;
        _configuration = configuration;
    }

    [BindProperty(SupportsGet = true)]
    public string? Email { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Provider { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ExternalDisplayName { get; private set; }

    public async Task<IActionResult> OnGet()
    {
        if (_signInManager.IsSignedIn(User))
        {
            return RedirectToPage("/Dashboard/Index");
        }

        if (string.Equals(Provider, "Google", StringComparison.OrdinalIgnoreCase))
        {
            var externalAuth = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
            if (externalAuth.Succeeded && externalAuth.Principal is not null)
            {
                ExternalDisplayName = externalAuth.Principal.FindFirstValue(ClaimTypes.Name)
                    ?? externalAuth.Principal.FindFirstValue("name");

                if (string.IsNullOrWhiteSpace(Email))
                {
                    Email = externalAuth.Principal.FindFirstValue(ClaimTypes.Email)
                        ?? externalAuth.Principal.FindFirstValue("email");
                }
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSendOtpAsync([FromForm] string email, [FromForm] string? fullName)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new JsonResult(new { success = false, message = "Email is required." }) { StatusCode = 400 };
        }

        var existing = await _userManager.FindByEmailAsync(email);
        if (existing != null)
        {
            return new JsonResult(new { success = false, message = "An account with this email already exists. Please sign in." }) { StatusCode = 400 };
        }

        var code = new Random().Next(100000, 999999).ToString();
        var ticket = new EmailOtpTicket
        {
            Email = email.Trim(),
            Code = code,
            Purpose = "signup",
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
        };
        _db.EmailOtpTickets.Add(ticket);
        await _db.SaveChangesAsync();

        var subject = "Your Bugence verification code";
        var body = $"<p>Hi {System.Net.WebUtility.HtmlEncode(fullName ?? "there")},</p>" +
                   $"<p>Your Bugence verification code is:</p>" +
                   $"<h2 style=\"letter-spacing:4px;\">{code}</h2>" +
                   "<p>This code expires in 15 minutes.</p>";
        var (sent, error) = await _emailSender.SendAsync(email, subject, body);

        return new JsonResult(new { success = true, warning = sent ? null : error });
    }

    public async Task<IActionResult> OnPostVerifyOtpAsync([FromForm] string email, [FromForm] string code)
    {
        var ticket = await _db.EmailOtpTickets
            .Where(t => t.Email == email && t.Purpose == "signup" && t.ConsumedAtUtc == null)
            .OrderByDescending(t => t.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (ticket == null || ticket.ExpiresAtUtc < DateTime.UtcNow)
        {
            return new JsonResult(new { success = false, message = "Verification code expired. Please resend." }) { StatusCode = 400 };
        }

        if (!string.Equals(ticket.Code, code, StringComparison.OrdinalIgnoreCase))
        {
            ticket.AttemptCount += 1;
            await _db.SaveChangesAsync();
            return new JsonResult(new { success = false, message = "Invalid verification code." }) { StatusCode = 400 };
        }

        ticket.ConsumedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new JsonResult(new { success = true });
    }

    public class RegisterPayload
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
        public string? ExternalProvider { get; set; }
        public string? Phone { get; set; }
        public string? RoleType { get; set; }
        public string? Company { get; set; }
        public string? CompanyName { get; set; }
        public string? CompanyAddressLine1 { get; set; }
        public string? CompanyAddressLine2 { get; set; }
        public string? CompanyCity { get; set; }
        public string? CompanyStateOrProvince { get; set; }
        public string? CompanyPostalCode { get; set; }
        public string? CompanyCountry { get; set; }
        public string? CompanyPhone { get; set; }
        public int? CompanyExpectedUsers { get; set; }
        public string PlanKey { get; set; } = "Starter";
        public string Interval { get; set; } = "Monthly";
        public bool TrialOptIn { get; set; }
        public string Provider { get; set; } = "local";
        public string? CardName { get; set; }
        public string? CardNumber { get; set; }
        public string? CardExpMonth { get; set; }
        public string? CardExpYear { get; set; }
    }

    public async Task<IActionResult> OnPostCreateAccountAsync([FromBody] RegisterPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Password))
        {
            return new JsonResult(new { success = false, message = "Password is required." }) { StatusCode = 400 };
        }

        var companyName = payload.CompanyName?.Trim();
        if (string.IsNullOrWhiteSpace(companyName))
        {
            companyName = payload.Company?.Trim();
        }
        if (string.IsNullOrWhiteSpace(companyName))
        {
            return new JsonResult(new { success = false, message = "Company name is required." }) { StatusCode = 400 };
        }

        if (!string.Equals(payload.Password, payload.ConfirmPassword, StringComparison.Ordinal))
        {
            return new JsonResult(new { success = false, message = "Password and confirm password do not match." }) { StatusCode = 400 };
        }

        ExternalLoginInfo? externalInfo = null;
        var isGoogleFlow = string.Equals(payload.ExternalProvider, "Google", StringComparison.OrdinalIgnoreCase);
        if (isGoogleFlow)
        {
            externalInfo = await _signInManager.GetExternalLoginInfoAsync();
            isGoogleFlow = externalInfo is not null
                && string.Equals(externalInfo.LoginProvider, "Google", StringComparison.OrdinalIgnoreCase);
        }

        if (!isGoogleFlow)
        {
            if (string.IsNullOrWhiteSpace(payload.FullName) || string.IsNullOrWhiteSpace(payload.Email))
            {
                return new JsonResult(new { success = false, message = "Please complete the required fields." }) { StatusCode = 400 };
            }

            var verified = await _db.EmailOtpTickets
                .Where(t => t.Email == payload.Email && t.Purpose == "signup" && t.ConsumedAtUtc != null)
                .OrderByDescending(t => t.ConsumedAtUtc)
                .FirstOrDefaultAsync();

            if (verified == null || verified.ConsumedAtUtc < DateTime.UtcNow.AddMinutes(-30))
            {
                return new JsonResult(new { success = false, message = "Please verify your email first." }) { StatusCode = 400 };
            }
        }
        else
        {
            var googleEmail = externalInfo?.Principal?.FindFirstValue(ClaimTypes.Email)
                ?? externalInfo?.Principal?.FindFirstValue("email");
            var googleName = externalInfo?.Principal?.FindFirstValue(ClaimTypes.Name)
                ?? externalInfo?.Principal?.FindFirstValue("name");

            if (!string.IsNullOrWhiteSpace(googleEmail))
            {
                payload.Email = googleEmail;
            }

            if (string.IsNullOrWhiteSpace(payload.FullName))
            {
                payload.FullName = googleName ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(payload.FullName))
            {
                payload.FullName = payload.Email.Split('@', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Bugence User";
            }

            if (string.IsNullOrWhiteSpace(payload.Email))
            {
                return new JsonResult(new { success = false, message = "Google profile email could not be resolved." }) { StatusCode = 400 };
            }
        }

        var existing = await _userManager.FindByEmailAsync(payload.Email);
        if (existing != null)
        {
            return new JsonResult(new { success = false, message = "Account already exists. Please sign in." }) { StatusCode = 400 };
        }

        var names = payload.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var company = new CompanyProfile
        {
            Name = companyName,
            AddressLine1 = payload.CompanyAddressLine1?.Trim(),
            AddressLine2 = payload.CompanyAddressLine2?.Trim(),
            City = payload.CompanyCity?.Trim(),
            StateOrProvince = payload.CompanyStateOrProvince?.Trim(),
            PostalCode = payload.CompanyPostalCode?.Trim(),
            Country = payload.CompanyCountry?.Trim(),
            PhoneNumber = payload.CompanyPhone?.Trim(),
            ExpectedUserCount = payload.CompanyExpectedUsers.HasValue && payload.CompanyExpectedUsers.Value > 0
                ? payload.CompanyExpectedUsers.Value
                : null
        };
        _db.CompanyProfiles.Add(company);
        await _db.SaveChangesAsync();

        var user = new ApplicationUser
        {
            UserName = payload.Email,
            Email = payload.Email,
            EmailConfirmed = true,
            DisplayName = payload.FullName,
            FirstName = names.FirstOrDefault(),
            LastName = names.Length > 1 ? string.Join(" ", names.Skip(1)) : null,
            PhoneNumber = payload.Phone,
            CompanyId = company.Id,
            IsCompanyAdmin = true
        };

        var createResult = await _userManager.CreateAsync(user, payload.Password);
        if (!createResult.Succeeded)
        {
            _db.CompanyProfiles.Remove(company);
            await _db.SaveChangesAsync();
            var msg = string.Join("; ", createResult.Errors.Select(e => e.Description));
            return new JsonResult(new { success = false, message = msg }) { StatusCode = 400 };
        }

        if (isGoogleFlow && externalInfo is not null)
        {
            var linkResult = await _userManager.AddLoginAsync(user, externalInfo);
            if (!linkResult.Succeeded)
            {
                var msg = string.Join("; ", linkResult.Errors.Select(e => e.Description));
                return new JsonResult(new { success = false, message = $"Google account could not be linked: {msg}" }) { StatusCode = 400 };
            }
        }

        var profile = new UserProfile
        {
            UserId = user.Id,
            BusinessName = company.Name,
            BusinessEmail = payload.Email,
            BusinessPhone = payload.CompanyPhone ?? payload.Phone
        };
        _db.UserProfiles.Add(profile);

        company.CreatedByUserId = user.Id;
        await CompanyDirectoryProvisioningService.EnsureUserCompanyRecordsAsync(
            _db,
            user,
            company,
            payload.FullName,
            payload.CompanyPhone ?? payload.Phone);

        var interval = payload.Interval?.ToLowerInvariant() switch
        {
            "6months" => SubscriptionInterval.SixMonths,
            "yearly" => SubscriptionInterval.Yearly,
            "trial" => SubscriptionInterval.Trial,
            _ => SubscriptionInterval.Monthly
        };

        if (payload.TrialOptIn)
        {
            await _subscriptionService.MarkSubscriptionAsync(user.Id, "Starter", SubscriptionInterval.Trial, "Trial", "Trial");
        }
        else if (string.Equals(payload.PlanKey, "Starter", StringComparison.OrdinalIgnoreCase))
        {
            await _subscriptionService.MarkSubscriptionAsync(user.Id, "Starter", SubscriptionInterval.Monthly, "Active", "Local");
        }
        else
        {
            await _subscriptionService.MarkSubscriptionAsync(user.Id, payload.PlanKey, interval, "Pending", payload.Provider);
        }

        if (string.Equals(payload.Provider, "local", StringComparison.OrdinalIgnoreCase) && !payload.TrialOptIn && !string.Equals(payload.PlanKey, "Starter", StringComparison.OrdinalIgnoreCase))
        {
            var last4 = string.IsNullOrWhiteSpace(payload.CardNumber) ? null : payload.CardNumber[^4..];
            var token = $"LOCAL-{Guid.NewGuid():N}";
            _db.UserPaymentMethods.Add(new UserPaymentMethod
            {
                UserId = user.Id,
                Provider = "Local",
                Last4 = last4,
                ExpMonth = payload.CardExpMonth,
                ExpYear = payload.CardExpYear,
                BillingNameEncrypted = _protector.Protect(payload.CardName ?? string.Empty),
                ProviderPaymentMethodIdEncrypted = _protector.Protect(token),
                IsDefault = true
            });
            await _subscriptionService.MarkSubscriptionAsync(user.Id, payload.PlanKey, interval, "Active", "Local");
        }

        await _db.SaveChangesAsync();

        if (!payload.TrialOptIn && !string.Equals(payload.PlanKey, "Starter", StringComparison.OrdinalIgnoreCase))
        {
            var checkout = await _paymentGateway.CreateCheckoutAsync(new CheckoutRequest(
                Provider: payload.Provider,
                PlanKey: payload.PlanKey,
                Interval: payload.Interval,
                ReturnUrl: "/Settings/Billing"));

            if (!checkout.Success)
            {
                return new JsonResult(new { success = false, message = checkout.Error ?? "Payment is not configured." }) { StatusCode = 400 };
            }

            return new JsonResult(new { success = true, redirectUrl = checkout.RedirectUrl });
        }

        var onboardingReturn = Uri.EscapeDataString("/Tools/Applications?onboarding=permissions");
        return new JsonResult(new { success = true, redirectUrl = $"/Auth/Login?signup=success&returnUrl={onboardingReturn}" });
    }
}
