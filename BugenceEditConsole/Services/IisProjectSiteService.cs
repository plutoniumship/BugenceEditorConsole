using System.Diagnostics;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace BugenceEditConsole.Services;

public interface IIisProjectSiteService
{
    Task EnsureProjectSiteAsync(ProjectDomain domain, UploadedProject project, CancellationToken cancellationToken = default);
    Task EnsureProjectHttpsAsync(ProjectDomain domain, UploadedProject project, string thumbprint, string storeName, string storeLocation, CancellationToken cancellationToken = default);
    Task RemoveDomainFromProjectSiteAsync(ProjectDomain domain, UploadedProject project, CancellationToken cancellationToken = default);
    Task DeleteProjectSiteIfUnusedAsync(int projectId, CancellationToken cancellationToken = default);
}

public class IisProjectSiteService : IIisProjectSiteService
{
    private readonly DomainRoutingOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<IisProjectSiteService> _logger;

    public IisProjectSiteService(IOptions<DomainRoutingOptions> options, IWebHostEnvironment environment, ILogger<IisProjectSiteService> logger)
    {
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task EnsureProjectSiteAsync(ProjectDomain domain, UploadedProject project, CancellationToken cancellationToken = default)
    {
        if (!CanManageSites() || string.IsNullOrWhiteSpace(domain.DomainName))
        {
            return;
        }

        var physicalPath = ResolveProjectPhysicalPath(project);
        if (string.IsNullOrWhiteSpace(physicalPath) || !Directory.Exists(physicalPath))
        {
            throw new InvalidOperationException($"Publish path not found for project {project.Id}.");
        }

        var siteName = ResolveSiteName(project.Id);
        var appPool = ResolveAppPoolName(project.Id);
        foreach (var hostHeader in ExpandHostVariants(domain.DomainName))
        {
            var script = string.Join('\n',
                "Import-Module WebAdministration;",
                "$ErrorActionPreference = 'Stop';",
                $"$siteName = '{EscapePs(siteName)}';",
                $"$appPool = '{EscapePs(appPool)}';",
                $"$hostHeader = '{EscapePs(hostHeader)}';",
                $"$path = '{EscapePs(physicalPath)}';",
                "",
                "if (-not (Test-Path $path)) {",
                "    throw 'Publish path does not exist: ' + $path;",
                "}",
                "",
                "if (-not (Test-Path ('IIS:\\AppPools\\' + $appPool))) {",
                "    New-WebAppPool -Name $appPool | Out-Null;",
                "}",
                "",
                "if (-not (Test-Path ('IIS:\\Sites\\' + $siteName))) {",
                "    New-Website -Name $siteName -PhysicalPath $path -ApplicationPool $appPool -Port 80 -HostHeader $hostHeader | Out-Null;",
                "} else {",
                "    Set-ItemProperty ('IIS:\\Sites\\' + $siteName) -Name physicalPath -Value $path;",
                "    Set-ItemProperty ('IIS:\\Sites\\' + $siteName) -Name applicationPool -Value $appPool;",
                "}",
                "",
                "$httpBindingInfo = '*:80:' + $hostHeader;",
                "$httpBindings = @(Get-WebBinding -Name $siteName -Protocol 'http' | Where-Object { $_.bindingInformation -eq $httpBindingInfo });",
                "if ($httpBindings.Count -eq 0) {",
                "    try {",
                "        New-WebBinding -Name $siteName -Protocol 'http' -Port 80 -HostHeader $hostHeader -ErrorAction Stop | Out-Null;",
                "    } catch {",
                "        if ($_.Exception.Message -notmatch '(?i)Cannot add duplicate collection entry') {",
                "            throw;",
                "        }",
                "    }",
                "    $httpBindings = @(Get-WebBinding -Name $siteName -Protocol 'http' | Where-Object { $_.bindingInformation -eq $httpBindingInfo });",
                "}",
                "if ($httpBindings.Count -gt 1) {",
                "    $httpBindings | Select-Object -Skip 1 | ForEach-Object {",
                "        Remove-WebBinding -Name $siteName -Protocol 'http' -Port $_.bindingInformation.Split(':')[1] -HostHeader $hostHeader -ErrorAction SilentlyContinue;",
                "    };",
                "}",
                "",
                "$site = Get-Website -Name $siteName;",
                "if ($site.state -ne 'Started') {",
                "    Start-Website -Name $siteName;",
                "}");

            await RunPowerShellAsync(script, cancellationToken);
        }
    }

    public async Task EnsureProjectHttpsAsync(ProjectDomain domain, UploadedProject project, string thumbprint, string storeName, string storeLocation, CancellationToken cancellationToken = default)
    {
        if (!CanManageSites() || string.IsNullOrWhiteSpace(domain.DomainName) || string.IsNullOrWhiteSpace(thumbprint))
        {
            return;
        }

        var siteName = ResolveSiteName(project.Id);
        foreach (var hostHeader in ExpandHostVariants(domain.DomainName))
        {
            var script = string.Join('\n',
                "Import-Module WebAdministration;",
                "$ErrorActionPreference = 'Stop';",
                $"$siteName = '{EscapePs(siteName)}';",
                $"$hostHeader = '{EscapePs(hostHeader)}';",
                $"$thumb = '{EscapePs(thumbprint)}';",
                $"$storeName = '{EscapePs(storeName)}';",
                $"$storeLocation = '{EscapePs(storeLocation)}';",
                "",
                "if (-not (Test-Path ('IIS:\\Sites\\' + $siteName))) {",
                "    throw 'IIS site not found: ' + $siteName;",
                "}",
                "",
                "$httpsBindingInfo = '*:443:' + $hostHeader;",
                "$existing = @(Get-WebBinding -Name $siteName -Protocol 'https' | Where-Object { $_.bindingInformation -eq $httpsBindingInfo });",
                "if ($existing.Count -eq 0) {",
                "    try {",
                "        New-WebBinding -Name $siteName -Protocol 'https' -Port 443 -HostHeader $hostHeader -SslFlags 1 -ErrorAction Stop | Out-Null;",
                "    } catch {",
                "        if ($_.Exception.Message -notmatch '(?i)Cannot add duplicate collection entry') {",
                "            throw;",
                "        }",
                "    }",
                "    $existing = @(Get-WebBinding -Name $siteName -Protocol 'https' | Where-Object { $_.bindingInformation -eq $httpsBindingInfo });",
                "}",
                "if ($existing.Count -gt 1) {",
                "    $existing | Select-Object -Skip 1 | ForEach-Object {",
                "        Remove-WebBinding -Name $siteName -Protocol 'https' -Port $_.bindingInformation.Split(':')[1] -HostHeader $hostHeader -ErrorAction SilentlyContinue;",
                "    };",
                "}",
                "$binding = Get-WebBinding -Name $siteName -Protocol 'https' | Where-Object { $_.bindingInformation -eq $httpsBindingInfo } | Select-Object -First 1;",
                "if ($null -eq $binding) {",
                "    throw 'HTTPS binding not found for host: ' + $hostHeader;",
                "}",
                "",
                "$certPath = \"Cert:\\$storeLocation\\$storeName\\$thumb\";",
                "if (-not (Test-Path $certPath)) {",
                "    throw 'Certificate not found in store: ' + $certPath;",
                "}",
                "",
                "$sniPath = 'IIS:\\SslBindings\\!443!' + $hostHeader;",
                "$ipPath = 'IIS:\\SslBindings\\0.0.0.0!443!' + $hostHeader;",
                "$wildPath = 'IIS:\\SslBindings\\*!443!' + $hostHeader;",
                "$clearHostSslBindings = {",
                "    foreach ($bindingPath in @($sniPath, $ipPath, $wildPath)) {",
                "        if (Test-Path $bindingPath) {",
                "            Remove-Item $bindingPath -Force -ErrorAction SilentlyContinue;",
                "        }",
                "    }",
                "    Get-ChildItem 'IIS:\\SslBindings' -ErrorAction SilentlyContinue |",
                "        Where-Object { $_.PSChildName -like ('*!443!' + $hostHeader) } |",
                "        ForEach-Object { Remove-Item $_.PSPath -Force -ErrorAction SilentlyContinue; };",
                "};",
                "& $clearHostSslBindings;",
                "$primaryError = $null;",
                "try {",
                "    $binding.AddSslCertificate($thumb, $storeName);",
                "} catch {",
                "    $primaryError = $_.Exception.Message;",
                "    & $clearHostSslBindings;",
                "    try {",
                "        Get-Item $certPath | New-Item $sniPath -SSLFlags 1 | Out-Null;",
                "    } catch {",
                "        throw ('Unable to attach SSL certificate for host ' + $hostHeader + '. Primary: ' + $primaryError + ' | Fallback: ' + $_.Exception.Message);",
                "    }",
                "}");

            await RunPowerShellAsync(script, cancellationToken);
        }
    }

    public async Task RemoveDomainFromProjectSiteAsync(ProjectDomain domain, UploadedProject project, CancellationToken cancellationToken = default)
    {
        if (!CanManageSites() || string.IsNullOrWhiteSpace(domain.DomainName))
        {
            return;
        }

        var siteName = ResolveSiteName(project.Id);
        foreach (var hostHeader in ExpandHostVariants(domain.DomainName))
        {
            var script = string.Join('\n',
                "Import-Module WebAdministration;",
                $"$siteName = '{EscapePs(siteName)}';",
                $"$hostHeader = '{EscapePs(hostHeader)}';",
                "if (-not (Test-Path ('IIS:\\Sites\\' + $siteName))) { return; }",
                "",
                "Get-WebBinding -Name $siteName | Where-Object { $_.HostHeader -eq $hostHeader } | ForEach-Object {",
                "    Remove-WebBinding -Name $siteName -Protocol $_.Protocol -Port $_.bindingInformation.Split(':')[1] -HostHeader $hostHeader;",
                "}",
                "",
                "$sniPath = 'IIS:\\SslBindings\\!443!' + $hostHeader;",
                "$ipPath = 'IIS:\\SslBindings\\0.0.0.0!443!' + $hostHeader;",
                "$wildPath = 'IIS:\\SslBindings\\*!443!' + $hostHeader;",
                "foreach ($bindingPath in @($sniPath, $ipPath, $wildPath)) {",
                "    if (Test-Path $bindingPath) {",
                "        Remove-Item $bindingPath -Force -ErrorAction SilentlyContinue;",
                "    }",
                "}",
                "Get-ChildItem 'IIS:\\SslBindings' -ErrorAction SilentlyContinue |",
                "    Where-Object { $_.PSChildName -like ('*!443!' + $hostHeader) } |",
                "    ForEach-Object { Remove-Item $_.PSPath -Force -ErrorAction SilentlyContinue; };");

            await RunPowerShellAsync(script, cancellationToken);
        }
    }

    public async Task DeleteProjectSiteIfUnusedAsync(int projectId, CancellationToken cancellationToken = default)
    {
        if (!CanManageSites() || projectId <= 0)
        {
            return;
        }

        var siteName = ResolveSiteName(projectId);
        var appPool = ResolveAppPoolName(projectId);

        var script = string.Join('\n',
            "Import-Module WebAdministration;",
            $"$siteName = '{EscapePs(siteName)}';",
            $"$appPool = '{EscapePs(appPool)}';",
            "",
            "if (Test-Path ('IIS:\\Sites\\' + $siteName)) {",
            "    Remove-Website -Name $siteName;",
            "}",
            "",
            "if (Test-Path ('IIS:\\AppPools\\' + $appPool)) {",
            "    Remove-WebAppPool -Name $appPool;",
            "}");

        await RunPowerShellAsync(script, cancellationToken);
    }

    private bool CanManageSites()
        => _options.AutoManageIisBindings && _options.PerProjectIisSites && OperatingSystem.IsWindows();

    private string ResolveSiteName(int projectId)
    {
        var pattern = string.IsNullOrWhiteSpace(_options.IisSiteNamePattern) ? "Bugence_{ProjectId}" : _options.IisSiteNamePattern;
        return pattern.Replace("{ProjectId}", projectId.ToString(), StringComparison.Ordinal);
    }

    private string ResolveAppPoolName(int projectId)
    {
        var pattern = string.IsNullOrWhiteSpace(_options.IisAppPoolPattern) ? "BugencePool_{ProjectId}" : _options.IisAppPoolPattern;
        return pattern.Replace("{ProjectId}", projectId.ToString(), StringComparison.Ordinal);
    }

    private string ResolveProjectPhysicalPath(UploadedProject project)
    {
        var relative = project.PublishStoragePath;
        if (string.IsNullOrWhiteSpace(relative))
        {
            var publishRoot = string.IsNullOrWhiteSpace(_options.PublishRoot)
                ? "Published"
                : _options.PublishRoot.Trim('\\', '/', ' ');
            if (!string.IsNullOrWhiteSpace(project.Slug))
            {
                relative = Path.Combine(publishRoot, "slugs", project.Slug);
            }
        }

        if (string.IsNullOrWhiteSpace(relative))
        {
            return string.Empty;
        }

        var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        return Path.Combine(webRoot, relative.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar));
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
            _logger.LogWarning("IIS project site command failed (exit {ExitCode}). Error: {Error}", process.ExitCode, error);
            throw new InvalidOperationException($"IIS command failed ({process.ExitCode}): {FirstMeaningfulLine(error)}");
        }

        if (!string.IsNullOrWhiteSpace(output))
        {
            _logger.LogDebug("IIS project site command output: {Output}", output);
        }
    }

    private static string EscapePs(string value)
        => value.Replace("'", "''", StringComparison.Ordinal);

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

        // Mirror only apex <-> www.apex for automatic dual-host support.
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
}
