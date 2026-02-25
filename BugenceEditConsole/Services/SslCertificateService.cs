using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Hosting;

namespace BugenceEditConsole.Services;

public record SslProvisionResult(
    bool Success,
    string? CertificatePath,
    string? PrivateKeyPath,
    string? ErrorMessage,
    string? CertificateThumbprint = null,
    string? PfxPath = null,
    string? PfxPassword = null,
    string? ErrorCode = null,
    string? ProviderUsed = null,
    string? FailureHint = null);

public interface ISslCertificateService
{
    Task<SslProvisionResult> ProvisionAsync(ProjectDomain domain, CancellationToken cancellationToken = default);
}

public class StubSslCertificateService : ISslCertificateService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<StubSslCertificateService> _logger;

    public StubSslCertificateService(IWebHostEnvironment environment, ILogger<StubSslCertificateService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<SslProvisionResult> ProvisionAsync(ProjectDomain domain, CancellationToken cancellationToken = default)
    {
        try
        {
            var root = Path.Combine(_environment.ContentRootPath, "App_Data", "certificates");
            Directory.CreateDirectory(root);

            var certPath = Path.Combine(root, $"{domain.Id}.crt");
            var keyPath = Path.Combine(root, $"{domain.Id}.key");

            await File.WriteAllTextAsync(certPath, $"FAKE CERTIFICATE for {domain.DomainName} generated at {DateTime.UtcNow:O}", cancellationToken);
            await File.WriteAllTextAsync(keyPath, $"FAKE PRIVATE KEY for {domain.DomainName}", cancellationToken);

            _logger.LogInformation("Issued placeholder certificate for {Domain}", domain.DomainName);
            return new SslProvisionResult(true, certPath, keyPath, null, null, null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create placeholder certificate for {Domain}", domain.DomainName);
            return new SslProvisionResult(false, null, null, ex.Message, null, null, null);
        }
    }
}
