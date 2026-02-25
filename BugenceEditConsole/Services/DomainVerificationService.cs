using BugenceEditConsole.Data;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BugenceEditConsole.Services;

public interface IDomainVerificationService
{
    Task VerifyPendingAsync(int batchSize, CancellationToken cancellationToken = default);
    Task<ProjectDomain?> VerifyDomainAsync(Guid domainId, CancellationToken cancellationToken = default);
}

public class DomainVerificationService : IDomainVerificationService
{
    private readonly ApplicationDbContext _db;
    private readonly IDomainRouter _router;
    private readonly ICertificateProvisioningOrchestrator _certProvisioner;
    private readonly DomainRoutingOptions _options;
    private readonly DomainObservabilityOptions _observability;
    private readonly DomainRoutingOptions _routingOptions;
    private readonly IWebHostEnvironment _environment;
    private readonly IIisDomainBindingService _iisBindings;
    private readonly IIisProjectSiteService _projectSites;
    private readonly ILogger<DomainVerificationService> _logger;
    private readonly LookupClient _lookupClient;
    private readonly INotificationService _notifications;

    public DomainVerificationService(
        ApplicationDbContext db,
        IDomainRouter router,
        ICertificateProvisioningOrchestrator certProvisioner,
        IOptions<DomainRoutingOptions> options,
        IOptions<DomainObservabilityOptions> observabilityOptions,
        IWebHostEnvironment environment,
        IIisDomainBindingService iisBindings,
        IIisProjectSiteService projectSites,
        INotificationService notifications,
        ILogger<DomainVerificationService> logger)
    {
        _db = db;
        _router = router;
        _certProvisioner = certProvisioner;
        _options = options.Value;
        _routingOptions = options.Value;
        _observability = observabilityOptions.Value;
        _environment = environment;
        _iisBindings = iisBindings;
        _projectSites = projectSites;
        _notifications = notifications;
        _logger = logger;
        _lookupClient = new LookupClient(new LookupClientOptions
        {
            UseCache = false,
            Timeout = TimeSpan.FromSeconds(5),
            ContinueOnDnsError = true
        });
    }

    public async Task VerifyPendingAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var domains = await _db.ProjectDomains
            .Include(d => d.Project)
            .Include(d => d.DnsRecords)
            .Where(d => d.DomainType == ProjectDomainType.Custom &&
                (d.Status == DomainStatus.Pending || d.Status == DomainStatus.Verifying))
            .OrderBy(d => d.UpdatedAtUtc)
            .Take(Math.Max(1, batchSize))
            .ToListAsync(cancellationToken);

        foreach (var domain in domains)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await VerifyDomainInternalAsync(domain, cancellationToken);
        }

        if (domains.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<ProjectDomain?> VerifyDomainAsync(Guid domainId, CancellationToken cancellationToken = default)
    {
        var domain = await _db.ProjectDomains
            .Include(d => d.Project)
            .Include(d => d.DnsRecords)
            .FirstOrDefaultAsync(d => d.Id == domainId, cancellationToken);
        if (domain is null)
        {
            return null;
        }

        await VerifyDomainInternalAsync(domain, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return domain;
    }

    private async Task VerifyDomainInternalAsync(ProjectDomain domain, CancellationToken cancellationToken)
    {
        if (domain.DomainType != ProjectDomainType.Custom)
        {
            return;
        }

        NormalizeDomainDnsRecords(domain);
        var records = domain.DnsRecords ?? new List<ProjectDomainDnsRecord>();
        var allSatisfied = true;
        foreach (var record in records)
        {
            var satisfied = await VerifyRecordAsync(record, cancellationToken);
            record.IsSatisfied = satisfied;
            record.LastCheckedAtUtc = DateTime.UtcNow;
            if (record.IsRequired && !satisfied)
            {
                allSatisfied = false;
            }
        }

        domain.LastCheckedAtUtc = DateTime.UtcNow;
        domain.UpdatedAtUtc = DateTime.UtcNow;

        var message = allSatisfied
            ? "DNS configuration detected."
            : "Awaiting required DNS records.";

        bool notificationSent;
        if (allSatisfied)
        {
            var publishReady = IsPublishReady(domain);
            if (!publishReady)
            {
                domain.Status = DomainStatus.Verifying;
                domain.FailureReason = "PUBLISH_NOT_READY|System|Awaiting project publish output.";
                message = "Awaiting project publish output.";
            }
            else
            {
                SslProvisionResult? sslResult = null;
                if (domain.SslStatus != DomainSslStatus.Active)
                {
                    sslResult = await ProvisionSslAsync(domain, cancellationToken);
                    if (!sslResult.Success)
                    {
                        message = sslResult.ErrorMessage ?? "SSL provisioning failed.";
                    }
                }

                if (domain.SslStatus == DomainSslStatus.Active)
                {
                    var provisioningReady = true;
                    var importedThumbprint = await EnsureCertificateImportedAsync(domain, sslResult, cancellationToken);
                    if (string.IsNullOrWhiteSpace(importedThumbprint))
                    {
                        domain.SslStatus = DomainSslStatus.Error;
                        domain.Status = DomainStatus.Verifying;
                        message = "SSL certificate import failed.";
                        provisioningReady = false;
                    }
                    else
                    {
                        try
                        {
                            if (_routingOptions.PerProjectIisSites)
                            {
                                if (domain.Project == null)
                                {
                                    message = "Project not found for IIS provisioning.";
                                    domain.Status = DomainStatus.Verifying;
                                    provisioningReady = false;
                                }
                                else
                                {
                                    await _projectSites.EnsureProjectSiteAsync(domain, domain.Project, cancellationToken);
                                    await _projectSites.EnsureProjectHttpsAsync(
                                        domain,
                                        domain.Project,
                                        importedThumbprint,
                                        _routingOptions.CertificateStoreName,
                                        _routingOptions.CertificateStoreLocation,
                                        cancellationToken);
                                }
                            }
                            else
                            {
                                await _iisBindings.EnsureHttpBindingAsync(domain.DomainName, cancellationToken);
                                await _iisBindings.EnsureHttpsBindingAsync(domain.DomainName, importedThumbprint, cancellationToken);
                            }
                        }
                        catch (Exception ex)
                        {
                            var shortReason = NormalizeIisFailureReason(ex);
                            domain.Status = DomainStatus.Verifying;
                            domain.SslStatus = DomainSslStatus.Error;
                            domain.FailureReason = $"IIS_BINDING_FAILED|System|{shortReason}";
                            message = $"IIS provisioning failed: {shortReason}";
                            provisioningReady = false;
                            _logger.LogWarning(ex, "IIS provisioning failed for {Domain}", domain.DomainName);
                        }
                    }

                    var previouslyUnverified = domain.Status != DomainStatus.Connected;
                    if (provisioningReady && domain.SslStatus == DomainSslStatus.Active)
                    {
                        domain.Status = DomainStatus.Connected;
                        domain.VerifiedAtUtc ??= DateTime.UtcNow;
                        domain.FailureReason = null;
                        if (previouslyUnverified)
                        {
                            _router.Evict(domain.DomainName);
                        }
                        message = "Domain connected and serving published output.";
                    }
                }
                else
                {
                    domain.Status = DomainStatus.Verifying;
                    message = string.IsNullOrWhiteSpace(message) ? "Awaiting SSL activation." : message;
                }
            }
        }
        else
        {
            domain.Status = DomainStatus.Verifying;
            domain.FailureReason = "DNS_NOT_READY|System|Awaiting required DNS records.";
        }

        notificationSent = await UpdateFailureStateAsync(domain, allSatisfied, message, cancellationToken);
        AppendLog(domain, allSatisfied, message, notificationSent);
    }

    private async Task<SslProvisionResult> ProvisionSslAsync(ProjectDomain domain, CancellationToken cancellationToken)
    {
        domain.SslStatus = DomainSslStatus.Provisioning;
        var result = await _certProvisioner.ProvisionAsync(domain, cancellationToken);
        if (result.Success)
        {
            domain.SslStatus = DomainSslStatus.Active;
            domain.CertificatePath = result.PfxPath ?? result.CertificatePath;
            domain.CertificateKeyPath = result.PfxPassword ?? result.PrivateKeyPath;
            domain.LastSslRenewalAtUtc = DateTime.UtcNow;
            domain.FailureReason = null;
        }
        else
        {
            domain.SslStatus = DomainSslStatus.Error;
            var code = string.IsNullOrWhiteSpace(result.ErrorCode) ? "CERT_ISSUE_FAILED" : result.ErrorCode;
            var reason = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "SSL provisioning failed." : result.ErrorMessage;
            var provider = string.IsNullOrWhiteSpace(result.ProviderUsed) ? "Webhook" : result.ProviderUsed;
            domain.FailureReason = $"{code}|{provider}|{reason}";
            _logger.LogWarning("SSL provisioning failed for {Domain}: {Code} {Reason}", domain.DomainName, code, reason);
        }
        return result;
    }

    private async Task<string?> EnsureCertificateImportedAsync(ProjectDomain domain, SslProvisionResult? sslResult, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pfxPath = sslResult?.PfxPath ?? domain.CertificatePath;
        if (string.IsNullOrWhiteSpace(pfxPath) || !File.Exists(pfxPath))
        {
            domain.FailureReason = "CERT_IMPORT_FAILED|PFX certificate file is missing.";
            return null;
        }

        try
        {
            var location = ParseStoreLocation(_routingOptions.CertificateStoreLocation);
            var thumbFromProvider = sslResult?.CertificateThumbprint?.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (!string.IsNullOrWhiteSpace(thumbFromProvider))
            {
                using var existingStore = new X509Store(_routingOptions.CertificateStoreName, location);
                existingStore.Open(OpenFlags.ReadOnly);
                var existingByThumb = existingStore.Certificates.Find(X509FindType.FindByThumbprint, thumbFromProvider, false);
                if (existingByThumb.Count > 0)
                {
                    domain.FailureReason = null;
                    return thumbFromProvider;
                }
            }

            var passwords = BuildPfxPasswordCandidates(sslResult?.PfxPassword, domain.CertificateKeyPath);
            var cert = TryLoadPfxCertificate(
                pfxPath,
                passwords,
                out var resolvedPassword,
                out var importError);
            if (cert is null)
            {
                var detail = importError?.GetBaseException().Message ?? "Unable to open PFX certificate.";
                domain.FailureReason = $"CERT_IMPORT_FAILED|System|{detail}";
                _logger.LogWarning(importError, "Certificate import failed for {Domain}", domain.DomainName);
                return null;
            }
            using var importedCert = cert;

            var thumbprint = importedCert.Thumbprint?.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                domain.FailureReason = "CERT_IMPORT_FAILED|Certificate thumbprint is missing.";
                return null;
            }

            using var store = new X509Store(_routingOptions.CertificateStoreName, location);
            store.Open(OpenFlags.ReadWrite);
            var existing = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
            if (existing.Count == 0)
            {
                store.Add(importedCert);
            }

            domain.CertificateKeyPath = resolvedPassword ?? string.Empty;
            domain.FailureReason = null;
            return thumbprint;
        }
        catch (Exception ex)
        {
            domain.FailureReason = $"CERT_IMPORT_FAILED|System|{ex.GetBaseException().Message}";
            _logger.LogWarning(ex, "Certificate import failed for {Domain}", domain.DomainName);
            return null;
        }
    }

    private static StoreLocation ParseStoreLocation(string value)
    {
        return Enum.TryParse<StoreLocation>(value, ignoreCase: true, out var parsed)
            ? parsed
            : StoreLocation.LocalMachine;
    }

    private static IEnumerable<string?> BuildPfxPasswordCandidates(string? primary, string? fallback)
    {
        static string? Normalize(string? value)
        {
            if (value is null)
            {
                return null;
            }

            var trimmed = value.Trim();
            if (trimmed.Length >= 2 &&
                trimmed.StartsWith("\"", StringComparison.Ordinal) &&
                trimmed.EndsWith("\"", StringComparison.Ordinal))
            {
                return trimmed[1..^1];
            }

            return trimmed;
        }

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in new[] { primary, fallback })
        {
            var normalized = Normalize(candidate);
            if (normalized is null)
            {
                continue;
            }

            if (emitted.Add(normalized))
            {
                yield return normalized;
            }
        }

        yield return string.Empty;
        yield return null;
    }

    private static X509Certificate2? TryLoadPfxCertificate(
        string pfxPath,
        IEnumerable<string?> passwords,
        out string? resolvedPassword,
        out Exception? lastError)
    {
        resolvedPassword = null;
        lastError = null;
        const X509KeyStorageFlags flags =
            X509KeyStorageFlags.MachineKeySet |
            X509KeyStorageFlags.PersistKeySet |
            X509KeyStorageFlags.Exportable;

        foreach (var password in passwords)
        {
            try
            {
                var cert = new X509Certificate2(pfxPath, password, flags);
                resolvedPassword = password;
                return cert;
            }
            catch (CryptographicException ex)
            {
                lastError = ex;
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        return null;
    }

    private static string NormalizeIisFailureReason(Exception ex)
    {
        var raw = ex.GetBaseException().Message;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "IIS command failed.";
        }

        var lines = raw
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(IsMeaningfulErrorLine)
            .ToList();

        var candidate = lines.Count == 0
            ? raw.Trim()
            : lines[0];

        if (LooksIncompleteErrorLine(candidate) && lines.Count > 1)
        {
            candidate = $"{candidate} {lines[1]}";
        }

        const string withCodePrefix = "IIS command failed (";
        const string genericPrefix = "IIS command failed:";
        if (candidate.StartsWith(withCodePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var markerIndex = candidate.IndexOf("):", StringComparison.Ordinal);
            if (markerIndex >= 0 && markerIndex + 2 < candidate.Length)
            {
                candidate = candidate[(markerIndex + 2)..].Trim();
            }
        }
        else if (candidate.StartsWith(genericPrefix, StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[genericPrefix.Length..].Trim();
        }

        if (candidate.Length > 180)
        {
            candidate = candidate[..180].TrimEnd();
        }

        return string.IsNullOrWhiteSpace(candidate) ? "IIS command failed." : candidate;
    }

    private void NormalizeDomainDnsRecords(ProjectDomain domain)
    {
        var records = domain.DnsRecords ??= new List<ProjectDomainDnsRecord>();
        var host = domain.DomainName.Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(domain.VerificationToken))
        {
            domain.VerificationToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(9)).ToLowerInvariant();
        }

        var token = domain.VerificationToken!;
        EnsureDnsRecord(records, "TXT", $"_bugence-verify.{host}", token, "ownership", required: true);
        EnsureDnsRecord(records, "TXT", $"_acme-challenge.{host}", token, "ssl", required: false);

        var useCnameRouting = !string.IsNullOrWhiteSpace(_routingOptions.WildcardRecordTarget);
        var cnameTarget = _routingOptions.WildcardRecordTarget?.Trim().TrimEnd('.');
        var edgeIp = _routingOptions.EdgeIpAddress?.Trim();
        var hostKey = NormalizeDnsName(host);

        var routingCnames = records
            .Where(r => string.Equals(r.Purpose, "routing", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.RecordType, "CNAME", StringComparison.OrdinalIgnoreCase) &&
                        NormalizeDnsName(r.Name) == hostKey)
            .ToList();
        var routingARecords = records
            .Where(r => string.Equals(r.Purpose, "routing", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.RecordType, "A", StringComparison.OrdinalIgnoreCase) &&
                        NormalizeDnsName(r.Name) == hostKey)
            .ToList();

        if (useCnameRouting && !string.IsNullOrWhiteSpace(cnameTarget))
        {
            var active = EnsureDnsRecord(records, "CNAME", host, cnameTarget, "routing", required: true);
            foreach (var stale in routingCnames.Where(r => r.Id != active.Id))
            {
                stale.IsRequired = false;
            }
        }
        else
        {
            foreach (var stale in routingCnames)
            {
                stale.IsRequired = false;
            }
        }

        if (!string.IsNullOrWhiteSpace(edgeIp))
        {
            var active = EnsureDnsRecord(records, "A", host, edgeIp, "routing", required: !useCnameRouting);
            foreach (var stale in routingARecords.Where(r => r.Id != active.Id))
            {
                stale.IsRequired = false;
            }
        }
        else
        {
            foreach (var stale in routingARecords)
            {
                stale.IsRequired = false;
            }
        }
    }

    private static ProjectDomainDnsRecord EnsureDnsRecord(
        ICollection<ProjectDomainDnsRecord> records,
        string recordType,
        string name,
        string value,
        string purpose,
        bool required)
    {
        var normalizedType = recordType.Trim().ToUpperInvariant();
        var normalizedPurpose = purpose.Trim();
        var normalizedName = NormalizeDnsName(name);

        var record = records.FirstOrDefault(r =>
            string.Equals(r.RecordType, normalizedType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Purpose, normalizedPurpose, StringComparison.OrdinalIgnoreCase) &&
            NormalizeDnsName(r.Name) == normalizedName);

        if (record is null)
        {
            record = new ProjectDomainDnsRecord
            {
                Id = Guid.NewGuid(),
                RecordType = normalizedType,
                Name = name,
                Value = value,
                Purpose = normalizedPurpose,
                IsRequired = required,
                CreatedAtUtc = DateTime.UtcNow
            };
            records.Add(record);
        }
        else
        {
            record.RecordType = normalizedType;
            record.Name = name;
            record.Value = value;
            record.Purpose = normalizedPurpose;
            record.IsRequired = required;
        }

        return record;
    }

    private static string NormalizeDnsName(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().TrimEnd('.').ToLowerInvariant();

    private static bool IsMeaningfulErrorLine(string line)
    {
        return !string.IsNullOrWhiteSpace(line) &&
            !line.StartsWith("At line:", StringComparison.OrdinalIgnoreCase) &&
            !line.StartsWith("+", StringComparison.Ordinal) &&
            !line.StartsWith("CategoryInfo", StringComparison.OrdinalIgnoreCase) &&
            !line.StartsWith("FullyQualifiedErrorId", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksIncompleteErrorLine(string line)
    {
        return line.EndsWith(":", StringComparison.Ordinal) ||
            line.EndsWith(",", StringComparison.Ordinal) ||
            line.EndsWith(";", StringComparison.Ordinal);
    }

    private async Task<bool> VerifyRecordAsync(ProjectDomainDnsRecord record, CancellationToken cancellationToken)
    {
        try
        {
            switch (record.RecordType.ToUpperInvariant())
            {
                case "TXT":
                    return await VerifyTxtAsync(record, cancellationToken);
                case "CNAME":
                    return await VerifyCnameAsync(record, cancellationToken);
                case "A":
                    return await VerifyAAsync(record, cancellationToken);
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DNS lookup failed for {Name} ({Type})", record.Name, record.RecordType);
            return false;
        }
    }

    private async Task<bool> VerifyTxtAsync(ProjectDomainDnsRecord record, CancellationToken cancellationToken)
    {
        var expected = record.Value?.Trim();
        if (string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        var response = await QueryWithAuthoritativeFallbackAsync(record.Name, QueryType.TXT, cancellationToken);
        return response.AllRecords.OfType<TxtRecord>()
            .SelectMany(r => r.Text)
            .Any(text => string.Equals(text?.Trim(), expected, StringComparison.Ordinal));
    }

    private async Task<bool> VerifyCnameAsync(ProjectDomainDnsRecord record, CancellationToken cancellationToken)
    {
        var expected = DomainUtilities.Normalize(record.Value);
        if (string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        var response = await QueryWithAuthoritativeFallbackAsync(record.Name, QueryType.CNAME, cancellationToken);
        var matches = response.AllRecords.OfType<CNameRecord>()
            .Where(entry => DomainUtilities.Normalize(entry.CanonicalName).Equals(expected, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            return false;
        }

        // Ensure CNAME target itself resolves publicly; matching a dead target
        // can otherwise produce false "connected" states and NXDOMAIN at runtime.
        try
        {
            var targetHost = expected.Trim().TrimEnd('.');
            var targetA = await _lookupClient.QueryAsync(targetHost, QueryType.A, cancellationToken: cancellationToken);
            if (targetA.Answers.OfType<ARecord>().Any())
            {
                return true;
            }

            var targetAaaa = await _lookupClient.QueryAsync(targetHost, QueryType.AAAA, cancellationToken: cancellationToken);
            return targetAaaa.Answers.OfType<AaaaRecord>().Any();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CNAME target resolution failed for {Name} -> {Target}", record.Name, expected);
            return false;
        }
    }

    private async Task<bool> VerifyAAsync(ProjectDomainDnsRecord record, CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(record.Value, out var expected))
        {
            return false;
        }

        var response = await QueryWithAuthoritativeFallbackAsync(record.Name, QueryType.A, cancellationToken);
        return response.AllRecords.OfType<ARecord>()
            .Any(entry => entry.Address.Equals(expected));
    }

    private async Task<IDnsQueryResponse> QueryWithAuthoritativeFallbackAsync(string name, QueryType type, CancellationToken cancellationToken)
    {
        var recursive = await _lookupClient.QueryAsync(name, type, cancellationToken: cancellationToken);
        // Gate readiness on recursive/public DNS visibility to avoid false "Connected"
        // states before propagation reaches resolvers used by end-users.
        return recursive;
    }

    private static string ExtractZone(string fqdn)
    {
        if (string.IsNullOrWhiteSpace(fqdn))
        {
            return string.Empty;
        }

        var name = fqdn.Trim().TrimEnd('.');
        var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return string.Empty;
        }

        // Basic fallback without public suffix list.
        return string.Join('.', parts[^2], parts[^1]);
    }

    private async Task<bool> UpdateFailureStateAsync(ProjectDomain domain, bool recordsSatisfied, string? message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (recordsSatisfied)
        {
            if (domain.ConsecutiveFailureCount > 0 || domain.LastFailureNotifiedAtUtc != null)
            {
                domain.ConsecutiveFailureCount = 0;
                domain.LastFailureNotifiedAtUtc = null;
            }
            return false;
        }

        domain.ConsecutiveFailureCount++;

        var threshold = Math.Max(1, _observability.FailureNotificationThreshold);
        if (domain.ConsecutiveFailureCount < threshold)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (domain.LastFailureNotifiedAtUtc.HasValue &&
            now - domain.LastFailureNotifiedAtUtc.Value < _observability.FailureNotificationCooldown)
        {
            return false;
        }

        var notified = await TrySendFailureNotificationAsync(domain, message, cancellationToken);
        if (notified)
        {
            domain.LastFailureNotifiedAtUtc = now;
        }
        return notified;
    }

    private async Task<bool> TrySendFailureNotificationAsync(ProjectDomain domain, string? message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var project = domain.Project;
        if (project is null)
        {
            project = await _db.UploadedProjects.FindAsync(new object?[] { domain.UploadedProjectId }, cancellationToken);
            if (project is not null)
            {
                domain.Project = project;
            }
        }

        var userId = project?.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var projectName = project?.DisplayName ?? project?.FolderName ?? $"Project #{domain.UploadedProjectId}";
        var title = $"Domain verification stalled for {domain.DomainName}";
        var descriptor = string.IsNullOrWhiteSpace(message) ? "Required DNS records have not been detected yet." : message;
        var body = $"{domain.DomainName} has failed verification {domain.ConsecutiveFailureCount} times in a row. {descriptor}";

        try
        {
            await _notifications.AddAsync(userId, title, body, "domain.verification", new
            {
                domain = domain.DomainName,
                projectId = domain.UploadedProjectId,
                project = projectName,
                failureCount = domain.ConsecutiveFailureCount,
                status = domain.Status.ToString(),
                sslStatus = domain.SslStatus.ToString()
            });
            _logger.LogInformation("Queued domain verification alert for {Domain}", domain.DomainName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to queue alert for {Domain}", domain.DomainName);
            return false;
        }
    }

    private void AppendLog(ProjectDomain domain, bool recordsSatisfied, string? message, bool notificationSent)
    {
        _db.DomainVerificationLogs.Add(new DomainVerificationLog
        {
            Id = Guid.NewGuid(),
            ProjectDomainId = domain.Id,
            Status = domain.Status,
            SslStatus = domain.SslStatus,
            AllRecordsSatisfied = recordsSatisfied,
            Message = string.IsNullOrWhiteSpace(message) ? null : message,
            FailureStreak = domain.ConsecutiveFailureCount,
            NotificationSent = notificationSent,
            CheckedAtUtc = DateTime.UtcNow
        });
    }

    private bool IsPublishReady(ProjectDomain domain)
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

        if (string.IsNullOrWhiteSpace(relative))
        {
            return false;
        }

        var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var physical = Path.Combine(webRoot, relative.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar));
        return Directory.Exists(physical) && File.Exists(Path.Combine(physical, "index.html"));
    }
}
