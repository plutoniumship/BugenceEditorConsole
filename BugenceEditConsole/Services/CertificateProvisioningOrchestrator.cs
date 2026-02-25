using BugenceEditConsole.Models;

namespace BugenceEditConsole.Services;

public interface ICertificateProvisioningOrchestrator
{
    Task<SslProvisionResult> ProvisionAsync(ProjectDomain domain, CancellationToken cancellationToken = default);
}

public class CertificateProvisioningOrchestrator : ICertificateProvisioningOrchestrator
{
    private static readonly string[] WebhookFallbackCodes =
    [
        "WEBHOOK_UNREACHABLE",
        "WEBHOOK_UNAUTHORIZED",
        "WEBHOOK_MISCONFIGURED",
        "CERT_ISSUE_FAILED"
    ];

    private readonly ISslCertificateService _primary;
    private readonly ILocalAcmeCertificateService _localAcme;
    private readonly ILogger<CertificateProvisioningOrchestrator> _logger;

    public CertificateProvisioningOrchestrator(
        ISslCertificateService primary,
        ILocalAcmeCertificateService localAcme,
        ILogger<CertificateProvisioningOrchestrator> logger)
    {
        _primary = primary;
        _localAcme = localAcme;
        _logger = logger;
    }

    public async Task<SslProvisionResult> ProvisionAsync(ProjectDomain domain, CancellationToken cancellationToken = default)
    {
        var webhookResult = await _primary.ProvisionAsync(domain, cancellationToken);
        if (webhookResult.Success)
        {
            return webhookResult with { ProviderUsed = string.IsNullOrWhiteSpace(webhookResult.ProviderUsed) ? "Webhook" : webhookResult.ProviderUsed };
        }

        if (!ShouldFallback(webhookResult))
        {
            return webhookResult with { ProviderUsed = string.IsNullOrWhiteSpace(webhookResult.ProviderUsed) ? "Webhook" : webhookResult.ProviderUsed };
        }

        _logger.LogWarning(
            "Webhook certificate provisioning failed for {Domain} with {Code}. Trying Local ACME fallback.",
            domain.DomainName,
            webhookResult.ErrorCode);

        var localResult = await _localAcme.ProvisionAsync(domain, cancellationToken);
        if (localResult.Success)
        {
            return localResult with { ProviderUsed = "LocalAcme" };
        }

        return localResult with
        {
            ProviderUsed = "LocalAcme",
            ErrorMessage = $"Webhook failed ({webhookResult.ErrorCode ?? "unknown"}): {webhookResult.ErrorMessage}. Local fallback failed: {localResult.ErrorMessage}",
            FailureHint = string.IsNullOrWhiteSpace(localResult.FailureHint) ? webhookResult.FailureHint : localResult.FailureHint
        };
    }

    private static bool ShouldFallback(SslProvisionResult result)
    {
        if (string.IsNullOrWhiteSpace(result.ErrorCode))
        {
            return true;
        }

        return WebhookFallbackCodes.Contains(result.ErrorCode, StringComparer.OrdinalIgnoreCase);
    }
}
