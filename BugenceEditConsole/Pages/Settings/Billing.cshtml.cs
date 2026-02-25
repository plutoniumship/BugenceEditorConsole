using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Settings;

public class BillingModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ISensitiveDataProtector _protector;

    public BillingModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ISubscriptionService subscriptionService, ISensitiveDataProtector protector)
    {
        _db = db;
        _userManager = userManager;
        _subscriptionService = subscriptionService;
        _protector = protector;
    }

    public UserSubscription? Subscription { get; private set; }
    public UserPaymentMethod? PaymentMethod { get; private set; }

    public async Task OnGetAsync()
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        Subscription = await _subscriptionService.GetOrStartTrialAsync(userId);
        PaymentMethod = await _db.UserPaymentMethods
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.IsDefault)
            .ThenByDescending(p => p.CreatedAtUtc)
            .FirstOrDefaultAsync();
    }

    public async Task<IActionResult> OnPostCheckoutAsync(string planKey, string interval, string provider, [FromServices] IPaymentGateway paymentGateway)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new JsonResult(new { success = false, message = "Unauthorized." }) { StatusCode = 401 };
        }

        await _subscriptionService.MarkSubscriptionAsync(userId, planKey, interval.ToLowerInvariant() switch
        {
            "6months" => SubscriptionInterval.SixMonths,
            "yearly" => SubscriptionInterval.Yearly,
            _ => SubscriptionInterval.Monthly
        }, "Pending", provider);

        var result = await paymentGateway.CreateCheckoutAsync(new CheckoutRequest(provider, planKey, interval, "/Settings/Billing"));
        if (!result.Success)
        {
            return new JsonResult(new { success = false, message = result.Error ?? "Payment not configured." }) { StatusCode = 400 };
        }

        return new JsonResult(new { success = true, redirectUrl = result.RedirectUrl });
    }

    public async Task<IActionResult> OnPostSavePaymentAsync(string provider, string cardholderName, string last4, int expMonth, int expYear, string paymentToken)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return RedirectToPage("/Auth/Login");
        }

        var cleanLast4 = new string((last4 ?? string.Empty).Where(char.IsDigit).ToArray());
        if (cleanLast4.Length >= 4)
        {
            cleanLast4 = cleanLast4[^4..];
        }

        if (cleanLast4.Length != 4 || expMonth < 1 || expMonth > 12 || expYear < DateTime.UtcNow.Year)
        {
            TempData["PaymentError"] = "Please enter a valid payment method.";
            return Redirect("/Settings/Billing?manage=payment");
        }

        var existing = await _db.UserPaymentMethods
            .Where(p => p.UserId == userId)
            .ToListAsync();
        if (existing.Any())
        {
            _db.UserPaymentMethods.RemoveRange(existing);
        }

        var method = new UserPaymentMethod
        {
            UserId = userId,
            Provider = string.IsNullOrWhiteSpace(provider) ? "Local" : provider,
            Last4 = cleanLast4,
            ExpMonth = expMonth.ToString("D2"),
            ExpYear = expYear.ToString(),
            BillingNameEncrypted = _protector.Protect(cardholderName ?? string.Empty),
            ProviderPaymentMethodIdEncrypted = _protector.Protect(paymentToken ?? "manual"),
            IsDefault = true
        };
        _db.UserPaymentMethods.Add(method);
        await _db.SaveChangesAsync();

        TempData["PaymentSaved"] = "Payment method saved.";
        return Redirect("/Settings/Billing?manage=payment&saved=1");
    }
}
