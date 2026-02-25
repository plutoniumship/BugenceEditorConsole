using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection("Webhook"));
builder.Services.AddSingleton<CertificateWebhookService>();

var app = builder.Build();
var startupValidation = app.Services.GetRequiredService<CertificateWebhookService>().ValidateStartup();

app.MapGet("/health", () => Results.Ok(new { ok = startupValidation.Ok, utc = DateTime.UtcNow, warnings = startupValidation.Warnings }));
app.MapGet("/health/details", (CertificateWebhookService service) => Results.Ok(service.BuildHealthDetails()));

app.MapPost("/api/certificates/issue", async (
    HttpRequest request,
    CertificateIssueRequest payload,
    CertificateWebhookService service,
    CancellationToken cancellationToken) =>
{
    var authHeader = request.Headers["X-API-Key"].ToString();
    var result = await service.IssueAsync(authHeader, payload, cancellationToken);
    if (!result.Success)
    {
        var status = result.Code switch
        {
            "UNAUTHORIZED" => StatusCodes.Status401Unauthorized,
            "MISCONFIGURED" => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Json(new
        {
            code = result.Code ?? "CERT_ISSUE_FAILED",
            message = result.ErrorMessage ?? "Certificate issue failed.",
            diagnostics = result.Diagnostics
        }, statusCode: status);
    }

    return Results.Ok(new
    {
        pfxBase64 = result.PfxBase64,
        pfxPassword = result.PfxPassword,
        thumbprint = result.Thumbprint
    });
});

app.Run();

sealed class CertificateWebhookService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<CertificateWebhookService> _logger;
    private readonly WebhookOptions _options;

    public CertificateWebhookService(
        IWebHostEnvironment environment,
        ILogger<CertificateWebhookService> logger,
        Microsoft.Extensions.Options.IOptions<WebhookOptions> options)
    {
        _environment = environment;
        _logger = logger;
        _options = options.Value;
    }

    public (bool Ok, List<string> Warnings) ValidateStartup()
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            warnings.Add("Webhook API key is not configured.");
        }

        if (!string.IsNullOrWhiteSpace(_options.IssueCommand))
        {
            var scriptPath = ExtractScriptPath(_options.IssueCommand);
            if (!string.IsNullOrWhiteSpace(scriptPath) && !File.Exists(scriptPath))
            {
                warnings.Add($"IssueCommand script not found: {scriptPath}");
            }
        }

        try
        {
            using var _ = OpenStore();
        }
        catch (Exception ex)
        {
            warnings.Add($"Certificate store check failed: {ex.GetBaseException().Message}");
        }

        return (warnings.Count == 0, warnings);
    }

    public object BuildHealthDetails()
    {
        var startup = ValidateStartup();
        var scriptPath = ExtractScriptPath(_options.IssueCommand);
        var storeReadable = true;
        string? storeError = null;
        try
        {
            using var _ = OpenStore();
        }
        catch (Exception ex)
        {
            storeReadable = false;
            storeError = ex.GetBaseException().Message;
        }

        return new
        {
            ok = startup.Ok,
            warnings = startup.Warnings,
            config = new
            {
                apiKeyConfigured = !string.IsNullOrWhiteSpace(_options.ApiKey),
                issueCommandConfigured = !string.IsNullOrWhiteSpace(_options.IssueCommand),
                issueScriptPath = scriptPath,
                issueScriptExists = string.IsNullOrWhiteSpace(scriptPath) || File.Exists(scriptPath),
                certificateStoreName = _options.CertificateStoreName,
                certificateStoreLocation = _options.CertificateStoreLocation,
                certificateStoreReadable = storeReadable,
                certificateStoreError = storeError
            },
            utc = DateTime.UtcNow
        };
    }

    public async Task<IssueResult> IssueAsync(string apiKeyHeader, CertificateIssueRequest payload, CancellationToken cancellationToken)
    {
        if (!IsAuthorized(apiKeyHeader))
        {
            return IssueResult.Fail("UNAUTHORIZED", "Unauthorized.", "Provide a valid X-API-Key header.");
        }

        var domain = NormalizeDomain(payload.Domain);
        if (string.IsNullOrWhiteSpace(domain))
        {
            return IssueResult.Fail("INVALID_DOMAIN", "Domain is required.", "Send a valid domain in request body.");
        }

        if (!string.IsNullOrWhiteSpace(_options.AllowedDomainPattern) &&
            !Regex.IsMatch(domain, _options.AllowedDomainPattern, RegexOptions.IgnoreCase))
        {
            return IssueResult.Fail("INVALID_DOMAIN", "Domain is not allowed by policy.", "Adjust allowed domain pattern.");
        }

        string? thumbprint = null;
        string? externalPfxPath = null;
        string? externalPfxPassword = null;
        string? externalPfxBase64 = null;

        if (!string.IsNullOrWhiteSpace(_options.IssueCommand))
        {
            var commandResult = await ExecuteIssueCommandAsync(domain, payload.Apex, cancellationToken);
            if (!commandResult.Success)
            {
                return IssueResult.Fail("CERT_ISSUE_FAILED", commandResult.ErrorMessage ?? "Issue command failed.", commandResult.Diagnostics);
            }

            thumbprint = commandResult.Thumbprint;
            externalPfxPath = commandResult.PfxPath;
            externalPfxPassword = commandResult.PfxPassword;
            externalPfxBase64 = commandResult.PfxBase64;
        }

        if (!string.IsNullOrWhiteSpace(externalPfxBase64))
        {
            return IssueResult.Ok(externalPfxBase64, externalPfxPassword ?? GeneratePassword(), thumbprint ?? string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(externalPfxPath))
        {
            if (!File.Exists(externalPfxPath))
            {
                return IssueResult.Fail("CERT_ISSUE_FAILED", $"PFX path not found: {externalPfxPath}", "Ensure issue command writes PFX to expected path.");
            }

            var bytes = await File.ReadAllBytesAsync(externalPfxPath, cancellationToken);
            return IssueResult.Ok(Convert.ToBase64String(bytes), externalPfxPassword ?? GeneratePassword(), thumbprint ?? string.Empty);
        }

        var cert = !string.IsNullOrWhiteSpace(thumbprint)
            ? FindByThumbprint(thumbprint)
            : FindBestCertificate(domain);

        if (cert is null)
        {
            return IssueResult.Fail("CERT_ISSUE_FAILED", "No usable certificate found. Configure IssueCommand or ensure certificate exists in store.", "Run /health/details and verify issue command + cert store.");
        }

        var password = GeneratePassword();
        byte[] pfxBytes;
        try
        {
            pfxBytes = cert.Export(X509ContentType.Pkcs12, password);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export certificate for {Domain}", domain);
            return IssueResult.Fail("CERT_EXPORT_FAILED", $"Failed to export certificate: {ex.GetBaseException().Message}", "Check certificate private key permissions.");
        }

        if (_options.SaveIssuedPfx)
        {
            var dir = Path.Combine(_environment.ContentRootPath, _options.IssuedPfxDirectory ?? "App_Data/issued-pfx");
            Directory.CreateDirectory(dir);
            var fileName = $"{domain}-{DateTime.UtcNow:yyyyMMddHHmmss}.pfx";
            await File.WriteAllBytesAsync(Path.Combine(dir, fileName), pfxBytes, cancellationToken);
        }

        return IssueResult.Ok(Convert.ToBase64String(pfxBytes), password, cert.Thumbprint ?? string.Empty);
    }

    private bool IsAuthorized(string value)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var expected = System.Text.Encoding.UTF8.GetBytes(_options.ApiKey);
        var supplied = System.Text.Encoding.UTF8.GetBytes(value);
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private async Task<CommandIssueResult> ExecuteIssueCommandAsync(string domain, string? apex, CancellationToken cancellationToken)
    {
        var command = _options.IssueCommand!
            .Replace("{domain}", domain, StringComparison.OrdinalIgnoreCase)
            .Replace("{apex}", apex ?? string.Empty, StringComparison.OrdinalIgnoreCase);

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

        var timeout = TimeSpan.FromSeconds(Math.Max(30, _options.IssueCommandTimeoutSeconds));
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
            return CommandIssueResult.Fail("Issue command timed out.");
        }

        if (process.ExitCode != 0)
        {
            return CommandIssueResult.Fail($"Issue command failed ({process.ExitCode}): {stderr}", "Check IssueCommand path and ACME client logs.");
        }

        if (string.IsNullOrWhiteSpace(stdout))
        {
            return CommandIssueResult.Fail("Issue command returned no output.", "Return JSON payload or ensure cert becomes available in store.");
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            return new CommandIssueResult
            {
                Success = true,
                Thumbprint = root.TryGetProperty("thumbprint", out var thumb) ? thumb.GetString() : null,
                PfxPath = root.TryGetProperty("pfxPath", out var pfxPath) ? pfxPath.GetString() : null,
                PfxPassword = root.TryGetProperty("pfxPassword", out var pfxPassword) ? pfxPassword.GetString() : null,
                PfxBase64 = root.TryGetProperty("pfxBase64", out var pfxBase64) ? pfxBase64.GetString() : null
            };
        }
        catch (Exception ex)
        {
            return CommandIssueResult.Fail($"Issue command output is not valid JSON: {ex.Message}", "Return JSON with thumbprint/pfxPath/pfxPassword/pfxBase64.");
        }
    }

    private static string ExtractScriptPath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var marker = "-File ";
        var index = command.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return string.Empty;
        }

        var tail = command[(index + marker.Length)..].Trim();
        if (tail.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = tail.IndexOf('"', 1);
            return end > 1 ? tail[1..end] : tail.Trim('"');
        }

        var parts = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : parts[0];
    }

    private X509Certificate2? FindByThumbprint(string thumbprint)
    {
        var cleaned = thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        using var store = OpenStore();
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, cleaned, validOnly: false);
        return found
            .OfType<X509Certificate2>()
            .Where(c => c.HasPrivateKey)
            .OrderByDescending(c => c.NotAfter)
            .FirstOrDefault();
    }

    private X509Certificate2? FindBestCertificate(string domain)
    {
        using var store = OpenStore();
        var now = DateTime.UtcNow;
        return store.Certificates
            .OfType<X509Certificate2>()
            .Where(c => c.HasPrivateKey && c.NotAfter.ToUniversalTime() > now.AddDays(7))
            .Where(c => MatchesDomain(c, domain))
            .OrderByDescending(c => c.NotAfter)
            .FirstOrDefault();
    }

    private X509Store OpenStore()
    {
        var location = Enum.TryParse<StoreLocation>(_options.CertificateStoreLocation, true, out var parsed)
            ? parsed
            : StoreLocation.LocalMachine;
        var name = _options.CertificateStoreName ?? "My";
        var store = new X509Store(name, location);
        store.Open(OpenFlags.ReadOnly);
        return store;
    }

    private static bool MatchesDomain(X509Certificate2 cert, string domain)
    {
        var dnsName = cert.GetNameInfo(X509NameType.DnsName, forIssuer: false);
        if (DomainMatch(dnsName, domain))
        {
            return true;
        }

        foreach (var extension in cert.Extensions)
        {
            if (!string.Equals(extension.Oid?.Value, "2.5.29.17", StringComparison.Ordinal))
            {
                continue;
            }

            var formatted = extension.Format(multiLine: false);
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

                var candidate = entry[prefix.Length..].Trim();
                if (DomainMatch(candidate, domain))
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

    private static string NormalizeDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return string.Empty;
        }

        var value = domain.Trim().ToLowerInvariant().TrimEnd('.');
        return value.Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string GeneratePassword()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', 'A').Replace('/', 'B');
    }
}

sealed class WebhookOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string CertificateStoreName { get; set; } = "My";
    public string CertificateStoreLocation { get; set; } = "LocalMachine";
    public string IssueCommand { get; set; } = string.Empty;
    public int IssueCommandTimeoutSeconds { get; set; } = 300;
    public string AllowedDomainPattern { get; set; } = "^[a-z0-9.-]+$";
    public bool SaveIssuedPfx { get; set; }
    public string IssuedPfxDirectory { get; set; } = "App_Data/issued-pfx";
}

sealed class CertificateIssueRequest
{
    public string Domain { get; set; } = string.Empty;
    public string Apex { get; set; } = string.Empty;
    public string? VerificationToken { get; set; }
    public List<DnsRecordRequest> DnsRecords { get; set; } = [];
}

sealed class DnsRecordRequest
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Required { get; set; }
}

sealed class IssueResult
{
    public bool Success { get; init; }
    public string? Code { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Diagnostics { get; init; }
    public string PfxBase64 { get; init; } = string.Empty;
    public string PfxPassword { get; init; } = string.Empty;
    public string Thumbprint { get; init; } = string.Empty;

    public static IssueResult Ok(string pfxBase64, string pfxPassword, string thumbprint) =>
        new() { Success = true, PfxBase64 = pfxBase64, PfxPassword = pfxPassword, Thumbprint = thumbprint };

    public static IssueResult Fail(string code, string message, string? diagnostics = null) =>
        new() { Success = false, Code = code, ErrorMessage = message, Diagnostics = diagnostics };
}

sealed class CommandIssueResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Diagnostics { get; init; }
    public string? Thumbprint { get; init; }
    public string? PfxPath { get; init; }
    public string? PfxPassword { get; init; }
    public string? PfxBase64 { get; init; }

    public static CommandIssueResult Fail(string message, string? diagnostics = null) => new() { Success = false, ErrorMessage = message, Diagnostics = diagnostics };
}
