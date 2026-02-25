namespace BugenceEditConsole.Services;

public record CheckoutRequest(
    string Provider,
    string PlanKey,
    string Interval,
    string ReturnUrl);

public record CheckoutResult(
    bool Success,
    string? RedirectUrl,
    string? Error);

public interface IPaymentGateway
{
    Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request);
    bool IsConfigured(string provider);
}

public class PaymentGateway : IPaymentGateway
{
    private readonly IConfiguration _config;

    public PaymentGateway(IConfiguration config)
    {
        _config = config;
    }

    public bool IsConfigured(string provider)
    {
        return provider.ToLowerInvariant() switch
        {
            "stripe" => !string.IsNullOrWhiteSpace(_config["Payments:Stripe:SecretKey"]),
            "paypal" => !string.IsNullOrWhiteSpace(_config["Payments:PayPal:ClientId"]),
            "local" => true,
            _ => false
        };
    }

    public Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request)
    {
        if (!IsConfigured(request.Provider))
        {
            return Task.FromResult(new CheckoutResult(false, null, $"{request.Provider} is not configured yet."));
        }

        // Placeholder implementation: wire your provider SDK here.
        var redirect = $"/Settings/Billing?checkout=ready&provider={Uri.EscapeDataString(request.Provider)}&plan={Uri.EscapeDataString(request.PlanKey)}&interval={Uri.EscapeDataString(request.Interval)}";
        return Task.FromResult(new CheckoutResult(true, redirect, null));
    }
}
