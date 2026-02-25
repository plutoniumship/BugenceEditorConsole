using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using BugenceEditConsole.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;

namespace BugenceEditConsole.Services;

public class WebhookSslCertificateService : ISslCertificateService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISensitiveDataProtector _protector;
    private readonly WebhookCertificateProviderOptions _options;
    private readonly ILogger<WebhookSslCertificateService> _logger;

    public WebhookSslCertificateService(
        IWebHostEnvironment environment,
        ApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        ISensitiveDataProtector protector,
        IOptions<CertificateProviderOptions> options,
        ILogger<WebhookSslCertificateService> logger)
    {
        _environment = environment;
        _db = db;
        _httpClientFactory = httpClientFactory;
        _protector = protector;
        _options = options.Value.Webhook;
        _logger = logger;
    }

    public async Task<SslProvisionResult> ProvisionAsync(ProjectDomain domain, CancellationToken cancellationToken = default)
    {
        var settings = await ResolveSettingsAsync(domain, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            return new SslProvisionResult(false, null, null, "Webhook endpoint is not configured. Add category 'CertificateWebhook' in System Properties or configure appsettings.", null, null, null, "WEBHOOK_MISCONFIGURED", "Webhook", "Create a valid CertificateWebhook config record or set env vars.");
        }

        try
        {
            var client = CreateClient(settings.ApiKey);
            var payload = new
            {
                domain = domain.DomainName,
                apex = domain.ApexRoot,
                verificationToken = domain.VerificationToken,
                dnsRecords = domain.DnsRecords.Select(r => new
                {
                    type = r.RecordType,
                    name = r.Name,
                    value = r.Value,
                    required = r.IsRequired
                })
            };

            using var response = await client.PostAsJsonAsync(settings.Endpoint, payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var code = response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden
                    ? "WEBHOOK_UNAUTHORIZED"
                    : response.StatusCode == HttpStatusCode.BadRequest
                        ? "CERT_ISSUE_FAILED"
                        : "WEBHOOK_UNREACHABLE";
                return new SslProvisionResult(false, null, null, $"Webhook returned {(int)response.StatusCode}: {body}", null, null, null, code, "Webhook", "Check webhook endpoint health and API key, then retry.");
            }

            var result = await response.Content.ReadFromJsonAsync<WebhookCertificateResponse>(cancellationToken: cancellationToken);
            if (result is null)
            {
                return new SslProvisionResult(false, null, null, "Webhook response was empty.", null, null, null, "WEBHOOK_UNREACHABLE", "Webhook", "Verify webhook response contract.");
            }

            var root = Path.Combine(_environment.ContentRootPath, _options.StorePath);
            Directory.CreateDirectory(root);

            var pfxPath = Path.Combine(root, $"{domain.Id}.pfx");
            var certPath = Path.Combine(root, $"{domain.Id}.crt");
            var keyPath = Path.Combine(root, $"{domain.Id}.key");

            if (_options.ExpectPfx)
            {
                if (string.IsNullOrWhiteSpace(result.PfxBase64))
                {
                    return new SslProvisionResult(false, null, null, "Webhook response missing pfxBase64.", null, null, null, "CERT_ISSUE_FAILED", "Webhook", "Return pfxBase64 and pfxPassword from webhook.");
                }

                byte[] pfxBytes;
                try
                {
                    pfxBytes = Convert.FromBase64String(result.PfxBase64);
                }
                catch (FormatException)
                {
                    return new SslProvisionResult(false, null, null, "Webhook pfxBase64 is invalid.", null, null, null, "CERT_ISSUE_FAILED", "Webhook", "Return valid base64 PKCS12 payload.");
                }

                await File.WriteAllBytesAsync(pfxPath, pfxBytes, cancellationToken);
                string? thumbprint = result.Thumbprint;
                if (string.IsNullOrWhiteSpace(thumbprint))
                {
                    try
                    {
                        var cert = new X509Certificate2(pfxBytes, result.PfxPassword ?? string.Empty, X509KeyStorageFlags.Exportable);
                        thumbprint = cert.Thumbprint;
                    }
                    catch
                    {
                        thumbprint = null;
                    }
                }

                _logger.LogInformation("Webhook PFX certificate stored for {Domain}", domain.DomainName);
                return new SslProvisionResult(
                    true,
                    pfxPath,
                    null,
                    null,
                    thumbprint,
                    pfxPath,
                    result.PfxPassword,
                    null,
                    "Webhook",
                    null);
            }

            if (string.IsNullOrWhiteSpace(result.Certificate) || string.IsNullOrWhiteSpace(result.PrivateKey))
            {
                return new SslProvisionResult(false, null, null, "Webhook response missing certificate or private key.", null, null, null, "CERT_ISSUE_FAILED", "Webhook", "Return complete PEM certificate and private key.");
            }

            await File.WriteAllTextAsync(certPath, result.Certificate, cancellationToken);
            await File.WriteAllTextAsync(keyPath, result.PrivateKey, cancellationToken);

            if (!string.IsNullOrWhiteSpace(result.Chain))
            {
                var chainPath = Path.Combine(root, $"{domain.Id}.chain.crt");
                await File.WriteAllTextAsync(chainPath, result.Chain, cancellationToken);
            }

            _logger.LogInformation("Webhook certificate stored for {Domain}", domain.DomainName);
            return new SslProvisionResult(true, certPath, keyPath, null, result.Thumbprint, null, null, null, "Webhook", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook certificate provisioning failed for {Domain}", domain.DomainName);
            return new SslProvisionResult(false, null, null, ex.Message, null, null, null, "WEBHOOK_UNREACHABLE", "Webhook", "Confirm webhook service availability and TLS reachability.");
        }
    }

    private async Task<(string Endpoint, string ApiKey)> ResolveSettingsAsync(ProjectDomain domain, CancellationToken cancellationToken)
    {
        try
        {
            var owner = domain.Project?.UserId;
            var company = domain.Project?.CompanyId;
            var fromSystemProperties = await SystemPropertyCertificateLoader.TryLoadWebhookSettingsAsync(_db, _protector, owner, company, cancellationToken);
            if (fromSystemProperties is { IsConfigured: true })
            {
                return (fromSystemProperties.Endpoint, fromSystemProperties.ApiKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load webhook certificate settings from SystemProperties. Falling back to appsettings.");
        }

        return (_options.Endpoint ?? string.Empty, _options.ApiKey ?? string.Empty);
    }

    private HttpClient CreateClient(string apiKey)
    {
        if (_options.IgnoreSslErrors)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            var client = new HttpClient(handler);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            }
            return client;
        }

        var http = _httpClientFactory.CreateClient("certificate-webhook");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            http.DefaultRequestHeaders.Remove("X-API-Key");
            http.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        }
        return http;
    }

    private sealed class WebhookCertificateResponse
    {
        public string? PfxBase64 { get; set; }
        public string? PfxPassword { get; set; }
        public string Certificate { get; set; } = string.Empty;
        public string PrivateKey { get; set; } = string.Empty;
        public string? Chain { get; set; }
        public string? Thumbprint { get; set; }
    }
}
