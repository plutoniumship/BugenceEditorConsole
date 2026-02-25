using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace BugenceEditConsole.Services;

public interface ILocalAcmeCertificateService
{
    Task<SslProvisionResult> ProvisionAsync(ProjectDomain domain, CancellationToken cancellationToken = default);
}

public class LocalAcmeCertificateService : ILocalAcmeCertificateService
{
    private readonly IWebHostEnvironment _environment;
    private readonly DomainRoutingOptions _routingOptions;
    private readonly LocalAcmeCertificateProviderOptions _options;
    private readonly ILogger<LocalAcmeCertificateService> _logger;

    public LocalAcmeCertificateService(
        IWebHostEnvironment environment,
        IOptions<DomainRoutingOptions> routingOptions,
        IOptions<CertificateProviderOptions> options,
        ILogger<LocalAcmeCertificateService> logger)
    {
        _environment = environment;
        _routingOptions = routingOptions.Value;
        _options = options.Value.LocalAcme;
        _logger = logger;
    }

    public async Task<SslProvisionResult> ProvisionAsync(ProjectDomain domain, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return new SslProvisionResult(false, null, null, "Local ACME fallback is disabled.", null, null, null, "CERT_ISSUE_FAILED", "LocalAcme", "Enable Certificates:LocalAcme:Enabled.");
        }

        if (string.IsNullOrWhiteSpace(domain.DomainName))
        {
            return new SslProvisionResult(false, null, null, "Domain is required.", null, null, null, "CERT_ISSUE_FAILED", "LocalAcme", "Provide a valid domain.");
        }

        try
        {
            var existing = FindBestCertificate(domain.DomainName);
            if (existing is not null)
            {
                var hasIssueCommand = !string.IsNullOrWhiteSpace(_options.IssueCommand);
                if (!IsSelfSigned(existing) || !hasIssueCommand)
                {
                    return await ExportAsPfxAsync(domain, existing, cancellationToken);
                }

                _logger.LogInformation(
                    "Existing certificate for {Domain} is self-signed; attempting ACME issuance before reuse.",
                    domain.DomainName);
            }

            if (string.IsNullOrWhiteSpace(_options.IssueCommand))
            {
                if (_options.AllowSelfSignedFallback)
                {
                    _logger.LogWarning("Local ACME issue command is not configured for {Domain}; issuing temporary self-signed certificate fallback.", domain.DomainName);
                    return await CreateSelfSignedFallbackAsync(domain, cancellationToken);
                }

                return new SslProvisionResult(false, null, null, "Local ACME issue command is not configured.", null, null, null, "CERT_ISSUE_FAILED", "LocalAcme", "Set Certificates:LocalAcme:IssueCommand.");
            }

            var issue = await ExecuteIssueCommandAsync(domain, cancellationToken);
            if (!issue.Success)
            {
                if (_options.AllowSelfSignedFallback)
                {
                    _logger.LogWarning("Local ACME command failed for {Domain}; issuing temporary self-signed certificate fallback.", domain.DomainName);
                    return await CreateSelfSignedFallbackAsync(domain, cancellationToken);
                }

                return new SslProvisionResult(false, null, null, issue.ErrorMessage, null, null, null, "CERT_ISSUE_FAILED", "LocalAcme", issue.FailureHint);
            }

            if (!string.IsNullOrWhiteSpace(issue.PfxBase64))
            {
                var pfxBytes = Convert.FromBase64String(issue.PfxBase64);
                var pfxPath = await SavePfxAsync(domain, pfxBytes, cancellationToken);
                var thumb = issue.Thumbprint;
                if (string.IsNullOrWhiteSpace(thumb))
                {
                    try
                    {
                        var cert = new X509Certificate2(pfxBytes, issue.PfxPassword ?? string.Empty, X509KeyStorageFlags.Exportable);
                        thumb = cert.Thumbprint;
                    }
                    catch
                    {
                        thumb = null;
                    }
                }

                return new SslProvisionResult(true, pfxPath, null, null, thumb, pfxPath, issue.PfxPassword, null, "LocalAcme", null);
            }

            if (!string.IsNullOrWhiteSpace(issue.PfxPath))
            {
                if (!File.Exists(issue.PfxPath))
                {
                    return new SslProvisionResult(false, null, null, $"Issued PFX file not found: {issue.PfxPath}", null, null, null, "CERT_ISSUE_FAILED", "LocalAcme", "Check LocalAcme command output.");
                }

                var bytes = await File.ReadAllBytesAsync(issue.PfxPath, cancellationToken);
                var saved = await SavePfxAsync(domain, bytes, cancellationToken);
                return new SslProvisionResult(true, saved, null, null, issue.Thumbprint, saved, issue.PfxPassword, null, "LocalAcme", null);
            }

            var certFromStore = !string.IsNullOrWhiteSpace(issue.Thumbprint)
                ? FindByThumbprint(issue.Thumbprint)
                : FindBestCertificate(domain.DomainName);

            if (certFromStore is null)
            {
                if (_options.AllowSelfSignedFallback)
                {
                    return await CreateSelfSignedFallbackAsync(domain, cancellationToken);
                }
                return new SslProvisionResult(false, null, null, "Certificate issuance succeeded but no usable certificate found in store.", null, null, null, "CERT_ISSUE_FAILED", "LocalAcme", "Check ACME issuance and certificate store permissions.");
            }

            return await ExportAsPfxAsync(domain, certFromStore, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Local ACME certificate provisioning failed for {Domain}", domain.DomainName);
            if (_options.AllowSelfSignedFallback)
            {
                try
                {
                    return await CreateSelfSignedFallbackAsync(domain, cancellationToken);
                }
                catch (Exception nested)
                {
                    _logger.LogError(nested, "Self-signed fallback failed for {Domain}", domain.DomainName);
                }
            }
            return new SslProvisionResult(false, null, null, ex.GetBaseException().Message, null, null, null, "CERT_ISSUE_FAILED", "LocalAcme", "Check win-acme command and server certificate permissions.");
        }
    }

    private async Task<SslProvisionResult> CreateSelfSignedFallbackAsync(ProjectDomain domain, CancellationToken cancellationToken)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            new X500DistinguishedName($"CN={domain.DomainName}"),
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(domain.DomainName);
        if (!string.IsNullOrWhiteSpace(domain.ApexRoot))
        {
            san.AddDnsName(domain.ApexRoot);
        }
        req.CertificateExtensions.Add(san.Build());

        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(90));
        var password = GeneratePassword();
        var pfxBytes = cert.Export(X509ContentType.Pkcs12, password);
        var path = await SavePfxAsync(domain, pfxBytes, cancellationToken);
        return new SslProvisionResult(
            true,
            path,
            null,
            null,
            cert.Thumbprint,
            path,
            password,
            null,
            "LocalAcme",
            "Temporary self-signed certificate issued; replace with ACME certificate for trusted HTTPS.");
    }

    private async Task<SslProvisionResult> ExportAsPfxAsync(ProjectDomain domain, X509Certificate2 cert, CancellationToken cancellationToken)
    {
        var password = GeneratePassword();
        var pfxBytes = cert.Export(X509ContentType.Pkcs12, password);
        var pfxPath = await SavePfxAsync(domain, pfxBytes, cancellationToken);
        return new SslProvisionResult(true, pfxPath, null, null, cert.Thumbprint, pfxPath, password, null, "LocalAcme", null);
    }

    private async Task<string> SavePfxAsync(ProjectDomain domain, byte[] pfxBytes, CancellationToken cancellationToken)
    {
        var root = Path.Combine(_environment.ContentRootPath, _options.StorePath);
        Directory.CreateDirectory(root);
        var pfxPath = Path.Combine(root, $"{domain.Id}.localacme.pfx");
        await File.WriteAllBytesAsync(pfxPath, pfxBytes, cancellationToken);
        return pfxPath;
    }

    private async Task<LocalCommandResult> ExecuteIssueCommandAsync(ProjectDomain domain, CancellationToken cancellationToken)
    {
        var webRoot = ResolveDomainWebRoot(domain);
        var command = _options.IssueCommand
            .Replace("{domain}", domain.DomainName, StringComparison.OrdinalIgnoreCase)
            .Replace("{apex}", domain.ApexRoot ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{challenge}", _options.PreferredChallenge, StringComparison.OrdinalIgnoreCase)
            .Replace("{webroot}", webRoot, StringComparison.OrdinalIgnoreCase);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var timeout = TimeSpan.FromSeconds(Math.Max(60, _options.TimeoutSeconds));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        string stdout;
        string stderr;
        try
        {
            stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
            stderr = await process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return LocalCommandResult.Fail("Local ACME issue command timed out.", "Increase Certificates:LocalAcme:TimeoutSeconds or verify ACME endpoint reachability.");
        }

        if (process.ExitCode != 0)
        {
            return LocalCommandResult.Fail($"Local ACME command failed ({process.ExitCode}): {stderr}", "Validate command path and ACME client availability.");
        }

        if (string.IsNullOrWhiteSpace(stdout))
        {
            return LocalCommandResult.SuccessResult();
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            return new LocalCommandResult
            {
                Success = true,
                Thumbprint = root.TryGetProperty("thumbprint", out var thumb) ? thumb.GetString() : null,
                PfxPath = root.TryGetProperty("pfxPath", out var pfxPath) ? pfxPath.GetString() : null,
                PfxPassword = root.TryGetProperty("pfxPassword", out var pfxPassword) ? pfxPassword.GetString() : null,
                PfxBase64 = root.TryGetProperty("pfxBase64", out var pfxBase64) ? pfxBase64.GetString() : null
            };
        }
        catch
        {
            return LocalCommandResult.SuccessResult();
        }
    }

    private string ResolveDomainWebRoot(ProjectDomain domain)
    {
        var relative = domain.Project?.PublishStoragePath;
        if (string.IsNullOrWhiteSpace(relative))
        {
            var publishRoot = string.IsNullOrWhiteSpace(_routingOptions.PublishRoot)
                ? "Published"
                : _routingOptions.PublishRoot.Trim('\\', '/', ' ');
            var slug = domain.Project?.Slug;
            if (!string.IsNullOrWhiteSpace(slug))
            {
                relative = Path.Combine(publishRoot, "slugs", slug);
            }
        }

        var baseWebRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        if (string.IsNullOrWhiteSpace(relative))
        {
            return baseWebRoot;
        }

        var normalized = relative.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalized)
            ? normalized
            : Path.Combine(baseWebRoot, normalized);
    }

    private X509Certificate2? FindByThumbprint(string thumbprint)
    {
        var cleaned = thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        using var store = OpenStore();
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, cleaned, validOnly: false);
        return found.OfType<X509Certificate2>().Where(c => c.HasPrivateKey).OrderByDescending(c => c.NotAfter).FirstOrDefault();
    }

    private X509Certificate2? FindBestCertificate(string domain)
    {
        using var store = OpenStore();
        var now = DateTime.UtcNow;
        return store.Certificates
            .OfType<X509Certificate2>()
            .Where(c => c.HasPrivateKey && c.NotAfter.ToUniversalTime() > now.AddDays(7))
            .Where(c => DomainMatch(c.GetNameInfo(X509NameType.DnsName, false), domain) || SanContains(c, domain))
            .OrderBy(c => IsSelfSigned(c) ? 1 : 0)
            .ThenByDescending(c => c.NotAfter)
            .FirstOrDefault();
    }

    private static bool IsSelfSigned(X509Certificate2 certificate)
        => string.Equals(certificate.Subject, certificate.Issuer, StringComparison.OrdinalIgnoreCase);

    private X509Store OpenStore()
    {
        var location = Enum.TryParse<StoreLocation>(_options.CertificateStoreLocation, true, out var parsed)
            ? parsed
            : StoreLocation.LocalMachine;
        var store = new X509Store(_options.CertificateStoreName ?? "My", location);
        store.Open(OpenFlags.ReadOnly);
        return store;
    }

    private static bool SanContains(X509Certificate2 cert, string domain)
    {
        foreach (var extension in cert.Extensions)
        {
            if (!string.Equals(extension.Oid?.Value, "2.5.29.17", StringComparison.Ordinal))
            {
                continue;
            }

            var formatted = extension.Format(false);
            if (string.IsNullOrWhiteSpace(formatted))
            {
                continue;
            }

            var entries = formatted.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var entry in entries)
            {
                const string prefix = "DNS Name=";
                if (!entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (DomainMatch(entry[prefix.Length..].Trim(), domain))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool DomainMatch(string? pattern, string domain)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        pattern = pattern.Trim().TrimEnd('.');
        domain = domain.Trim().TrimEnd('.');

        if (string.Equals(pattern, domain, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (pattern.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = pattern[1..];
            return domain.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                   domain.Count(c => c == '.') >= pattern.Count(c => c == '.');
        }

        return false;
    }

    private static string GeneratePassword()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', 'A').Replace('/', 'B');
    }

    private sealed class LocalCommandResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public string? FailureHint { get; init; }
        public string? Thumbprint { get; init; }
        public string? PfxPath { get; init; }
        public string? PfxPassword { get; init; }
        public string? PfxBase64 { get; init; }

        public static LocalCommandResult SuccessResult() => new() { Success = true };
        public static LocalCommandResult Fail(string message, string hint) => new() { Success = false, ErrorMessage = message, FailureHint = hint };
    }
}
