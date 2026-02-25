using System.Diagnostics;
using BugenceEditConsole.Infrastructure;
using Microsoft.Extensions.Options;

namespace BugenceEditConsole.Services;

public interface IIisDomainBindingService
{
    Task EnsureHttpBindingAsync(string domain, CancellationToken cancellationToken = default);
    Task EnsureHttpsBindingAsync(string domain, string? certificateThumbprint, CancellationToken cancellationToken = default);
    Task RemoveBindingsAsync(string domain, CancellationToken cancellationToken = default);
}

public class IisDomainBindingService : IIisDomainBindingService
{
    private readonly DomainRoutingOptions _options;
    private readonly ILogger<IisDomainBindingService> _logger;

    public IisDomainBindingService(IOptions<DomainRoutingOptions> options, ILogger<IisDomainBindingService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task EnsureHttpBindingAsync(string domain, CancellationToken cancellationToken = default)
    {
        if (!CanManageBindings() || string.IsNullOrWhiteSpace(domain))
        {
            return;
        }

        var escapedSite = EscapePs(_options.IisSiteName);
        foreach (var hostHeader in ExpandHostVariants(domain))
        {
            var escapedDomain = EscapePs(hostHeader);
            var script = $@"
Import-Module WebAdministration;
$ErrorActionPreference = 'Stop';
$site = '{escapedSite}';
$hostHeader = '{escapedDomain}';
$httpBindingInfo = ""*:80:$hostHeader"";
$existing = @(Get-WebBinding -Name $site -Protocol 'http' | Where-Object {{ $_.bindingInformation -eq $httpBindingInfo }});
if ($existing.Count -eq 0) {{
    try {{
        New-WebBinding -Name $site -Protocol 'http' -Port 80 -HostHeader $hostHeader -ErrorAction Stop | Out-Null;
    }} catch {{
        if ($_.Exception.Message -notmatch '(?i)Cannot add duplicate collection entry') {{
            throw;
        }}
    }}
    $existing = @(Get-WebBinding -Name $site -Protocol 'http' | Where-Object {{ $_.bindingInformation -eq $httpBindingInfo }});
}}
if ($existing.Count -gt 1) {{
    $existing | Select-Object -Skip 1 | ForEach-Object {{
        Remove-WebBinding -Name $site -Protocol 'http' -Port $_.bindingInformation.Split(':')[1] -HostHeader $hostHeader -ErrorAction SilentlyContinue;
    }};
}}";
            await RunPowerShellAsync(script, cancellationToken);
        }
    }

    public async Task EnsureHttpsBindingAsync(string domain, string? certificateThumbprint, CancellationToken cancellationToken = default)
    {
        if (!CanManageBindings() || string.IsNullOrWhiteSpace(domain))
        {
            return;
        }

        var escapedSite = EscapePs(_options.IisSiteName);
        var storeName = EscapePs(_options.CertificateStoreName);
        var storeLocation = EscapePs(_options.CertificateStoreLocation);
        var thumb = EscapePs(certificateThumbprint ?? string.Empty);
        foreach (var hostHeader in ExpandHostVariants(domain))
        {
            var escapedDomain = EscapePs(hostHeader);
            var script = $@"
Import-Module WebAdministration;
$ErrorActionPreference = 'Stop';
$site = '{escapedSite}';
$hostHeader = '{escapedDomain}';
$thumb = '{thumb}';
$storeName = '{storeName}';
$storeLocation = '{storeLocation}';
$httpsBindingInfo = ""*:443:$hostHeader"";
$existing = @(Get-WebBinding -Name $site -Protocol 'https' | Where-Object {{ $_.bindingInformation -eq $httpsBindingInfo }});
if ($existing.Count -eq 0) {{
    try {{
        New-WebBinding -Name $site -Protocol 'https' -Port 443 -HostHeader $hostHeader -SslFlags 1 -ErrorAction Stop | Out-Null;
    }} catch {{
        if ($_.Exception.Message -notmatch '(?i)Cannot add duplicate collection entry') {{
            throw;
        }}
    }}
    $existing = @(Get-WebBinding -Name $site -Protocol 'https' | Where-Object {{ $_.bindingInformation -eq $httpsBindingInfo }});
}}
if ($existing.Count -gt 1) {{
    $existing | Select-Object -Skip 1 | ForEach-Object {{
        Remove-WebBinding -Name $site -Protocol 'https' -Port $_.bindingInformation.Split(':')[1] -HostHeader $hostHeader -ErrorAction SilentlyContinue;
    }};
}}
$binding = Get-WebBinding -Name $site -Protocol 'https' | Where-Object {{ $_.bindingInformation -eq $httpsBindingInfo }} | Select-Object -First 1;
if ($null -eq $binding) {{
    throw ""HTTPS binding not found for host: $hostHeader"";
}}
if ($thumb -and $thumb.Length -gt 0) {{
    $certPath = ""Cert:\$storeLocation\$storeName\$thumb"";
    if (-not (Test-Path $certPath)) {{
        throw ""Certificate not found in store: $certPath"";
    }}
    $sniPath = ""IIS:\SslBindings\!443!$hostHeader"";
    $ipPath = ""IIS:\SslBindings\0.0.0.0!443!$hostHeader"";
    $wildPath = ""IIS:\SslBindings\*!443!$hostHeader"";
    $clearHostSslBindings = {{
        foreach ($bindingPath in @($sniPath, $ipPath, $wildPath)) {{
            if (Test-Path $bindingPath) {{
                Remove-Item $bindingPath -Force -ErrorAction SilentlyContinue;
            }}
        }}
        Get-ChildItem 'IIS:\SslBindings' -ErrorAction SilentlyContinue |
            Where-Object {{ $_.PSChildName -like (""*!443!"" + $hostHeader) }} |
            ForEach-Object {{ Remove-Item $_.PSPath -Force -ErrorAction SilentlyContinue; }};
    }};
    & $clearHostSslBindings;
    $primaryError = $null;
    try {{
        $binding.AddSslCertificate($thumb, $storeName);
    }} catch {{
        $primaryError = $_.Exception.Message;
        & $clearHostSslBindings;
        try {{
            Get-Item $certPath | New-Item $sniPath -SSLFlags 1 | Out-Null;
        }} catch {{
            throw ""Unable to attach SSL certificate for host $hostHeader. Primary: $primaryError | Fallback: $($_.Exception.Message)"";
        }}
    }}
}}";
            await RunPowerShellAsync(script, cancellationToken);
        }
    }

    public async Task RemoveBindingsAsync(string domain, CancellationToken cancellationToken = default)
    {
        if (!CanManageBindings() || string.IsNullOrWhiteSpace(domain))
        {
            return;
        }

        var escapedSite = EscapePs(_options.IisSiteName);
        foreach (var hostHeader in ExpandHostVariants(domain))
        {
            var escapedDomain = EscapePs(hostHeader);
            var script = $@"
Import-Module WebAdministration;
$site = '{escapedSite}';
$hostHeader = '{escapedDomain}';
Get-WebBinding -Name $site | Where-Object {{ $_.HostHeader -eq $hostHeader }} | ForEach-Object {{
    Remove-WebBinding -Name $site -Protocol $_.protocol -Port $_.bindingInformation.Split(':')[1] -HostHeader $hostHeader;
}}
$sniPath = ""IIS:\SslBindings\!443!$hostHeader"";
$ipPath = ""IIS:\SslBindings\0.0.0.0!443!$hostHeader"";
$wildPath = ""IIS:\SslBindings\*!443!$hostHeader"";
foreach ($bindingPath in @($sniPath, $ipPath, $wildPath)) {{
    if (Test-Path $bindingPath) {{
        Remove-Item $bindingPath -Force -ErrorAction SilentlyContinue;
    }}
}}
Get-ChildItem 'IIS:\SslBindings' -ErrorAction SilentlyContinue |
    Where-Object {{ $_.PSChildName -like (""*!443!"" + $hostHeader) }} |
    ForEach-Object {{ Remove-Item $_.PSPath -Force -ErrorAction SilentlyContinue; }}";
            await RunPowerShellAsync(script, cancellationToken);
        }
    }

    private bool CanManageBindings()
    {
        return _options.AutoManageIisBindings && OperatingSystem.IsWindows();
    }

    private async Task RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            _logger.LogWarning("IIS binding command failed (exit {ExitCode}). Error: {Error}", process.ExitCode, error);
            throw new InvalidOperationException($"IIS command failed ({process.ExitCode}): {FirstMeaningfulLine(error)}");
        }

        if (!string.IsNullOrWhiteSpace(output))
        {
            _logger.LogDebug("IIS binding command output: {Output}", output);
        }
    }

    private static string EscapePs(string value) => value.Replace("'", "''");

    private static string FirstMeaningfulLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Unknown IIS error.";
        }

        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (!IsMeaningfulErrorLine(trimmed))
            {
                continue;
            }

            if (LooksIncompleteErrorLine(trimmed))
            {
                for (var j = i + 1; j < lines.Length; j++)
                {
                    var detail = lines[j].Trim();
                    if (IsMeaningfulErrorLine(detail))
                    {
                        return $"{trimmed} {detail}";
                    }
                }
            }

            return trimmed;
        }

        return text.Trim();
    }

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

    private static IReadOnlyList<string> ExpandHostVariants(string domain)
    {
        var host = DomainUtilities.Normalize(domain);
        if (string.IsNullOrWhiteSpace(host))
        {
            return Array.Empty<string>();
        }

        var apex = DomainUtilities.GetApex(host);
        if (string.IsNullOrWhiteSpace(apex))
        {
            return new[] { host };
        }

        // Only mirror apex<->www for root domains, not arbitrary subdomains.
        if (string.Equals(host, apex, StringComparison.OrdinalIgnoreCase))
        {
            return new[] { host, "www." + apex };
        }

        if (string.Equals(host, "www." + apex, StringComparison.OrdinalIgnoreCase))
        {
            return new[] { host, apex };
        }

        return new[] { host };
    }
}
