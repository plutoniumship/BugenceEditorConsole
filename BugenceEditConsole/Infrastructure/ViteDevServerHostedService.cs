using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Hosting;

namespace BugenceEditConsole.Infrastructure;

/// <summary>
/// Development-only hosted service that ensures the Vite dashboard dev server is running
/// whenever the ASP.NET application starts from Visual Studio.
/// </summary>
public sealed class ViteDevServerHostedService : IHostedService, IDisposable
{
    private readonly ILogger<ViteDevServerHostedService> _logger;
    private readonly IHostEnvironment _environment;
    private readonly string _workingDirectory;
    private readonly Uri _serverUri;
    private Process? _viteProcess;
    private CancellationTokenSource? _startupMonitorCts;
    private Task? _startupMonitorTask;
    private readonly HttpClient _httpClient;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);
    private static readonly string[] WindowsExecutableExtensions = [".cmd", ".exe", ".bat"];
    private const string PnpmExecutableOverrideEnv = "BUGENCE_PNPM_EXECUTABLE";
    private const string AdditionalArgsEnv = "BUGENCE_VITE_PNPM_ARGS";

    public ViteDevServerHostedService(
        ILogger<ViteDevServerHostedService> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
        _workingDirectory = Path.GetFullPath(Path.Combine(environment.ContentRootPath, ".."));
        _serverUri = new Uri(
            Environment.GetEnvironmentVariable("BUGENCE_VITE_SERVER_URL")
            ?? "http://localhost:5173",
            UriKind.Absolute);

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return;
        }

        if (string.Equals(
                Environment.GetEnvironmentVariable("BUGENCE_SKIP_VITE_DEV"),
                "1",
                StringComparison.Ordinal))
        {
            _logger.LogInformation("Skipping Vite dashboard dev server launch (BUGENCE_SKIP_VITE_DEV=1).");
            return;
        }

        if (!Directory.Exists(_workingDirectory))
        {
            _logger.LogWarning(
                "Unable to start Vite dev server because working directory '{WorkingDirectory}' was not found.",
                _workingDirectory);
            return;
        }

        if (await IsServerListeningAsync(cancellationToken))
        {
            _logger.LogInformation("Detected existing Vite dev server at {ServerUrl}.", _serverUri);
            return;
        }

        try
        {
            EnsureViteExecutablePermissions();

            var startInfo = CreatePnpmProcessStartInfo();
            if (startInfo is null)
            {
                _logger.LogWarning(
                    "Skipping Vite dev server launch because pnpm could not be located. " +
                    "Install pnpm (e.g. `corepack enable pnpm`) or set {EnvVar} to the pnpm executable path.",
                    PnpmExecutableOverrideEnv);
                return;
            }

            _logger.LogInformation(
                "Starting Vite dashboard dev server at {ServerUrl} using command: {Command} {Arguments}",
                _serverUri,
                startInfo.FileName,
                startInfo.Arguments);

            _viteProcess = Process.Start(startInfo);
            if (_viteProcess is null)
            {
                _logger.LogWarning("Failed to start Vite dev server process.");
                return;
            }

            _viteProcess.OutputDataReceived += OnOutputDataReceived;
            _viteProcess.ErrorDataReceived += OnErrorDataReceived;
            _viteProcess.BeginOutputReadLine();
            _viteProcess.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch the Vite dev server.");
            return;
        }

        // Never block ASP.NET startup on frontend tooling.
        _startupMonitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _startupMonitorTask = MonitorStartupAsync(_startupMonitorCts.Token);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_startupMonitorCts is not null)
        {
            _startupMonitorCts.Cancel();
        }

        if (_viteProcess is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            if (!_viteProcess.HasExited)
            {
                _logger.LogInformation("Stopping Vite dev server ...");
                _viteProcess.Kill(entireProcessTree: true);
                _viteProcess.WaitForExit((int)TimeSpan.FromSeconds(5).TotalMilliseconds);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while stopping the Vite dev server.");
        }
        finally
        {
            _viteProcess.Dispose();
            _viteProcess = null;
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _startupMonitorCts?.Dispose();
        _httpClient.Dispose();
        _viteProcess?.Dispose();
    }

    private async Task MonitorStartupAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + StartupTimeout;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (_viteProcess is { HasExited: true })
            {
                _logger.LogWarning(
                    "Vite dev server process exited with code {ExitCode}. ASP.NET app continues without Vite.",
                    _viteProcess.ExitCode);
                return;
            }

            if (await IsServerListeningAsync(cancellationToken))
            {
                _logger.LogInformation("Vite dev server is ready at {ServerUrl}.", _serverUri);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Timed out waiting for the Vite dev server at {ServerUrl}. ASP.NET app continues without Vite.",
                _serverUri);
        }
    }

    private void EnsureViteExecutablePermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var viteBinaries = new[]
        {
            Path.Combine(_workingDirectory, "packages", "dashboard", "node_modules", ".bin", "vite"),
            Path.Combine(_workingDirectory, "packages", "canvas", "node_modules", ".bin", "vite")
        };

        foreach (var binaryPath in viteBinaries)
        {
            if (!File.Exists(binaryPath))
            {
                continue;
            }

            try
            {
                var mode = File.GetUnixFileMode(binaryPath);
                var executableBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                if ((mode & executableBits) != executableBits)
                {
                    File.SetUnixFileMode(binaryPath, mode | executableBits);
                    _logger.LogInformation("Added execute permission to {Path}.", binaryPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to update executable permissions for {Path}.", binaryPath);
            }
        }
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.Data))
        {
            _logger.LogInformation("[vite] {Message}", args.Data);
        }
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs args)
    {
        if (!string.IsNullOrWhiteSpace(args.Data))
        {
            _logger.LogWarning("[vite] {Message}", args.Data);
        }
    }

    private async Task<bool> IsServerListeningAsync(CancellationToken cancellationToken)
    {
        foreach (var healthUri in GetHealthCheckUris())
        {
            try
            {
                using var response = await _httpClient.GetAsync(healthUri, cancellationToken);
                // Any non-exceptional response means the dev server accepted the connection.
                return true;
            }
            catch (HttpRequestException)
            {
                // Try the next candidate endpoint.
                continue;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }
        }

        return false;
    }

    private IEnumerable<Uri> GetHealthCheckUris()
    {
        yield return new Uri(_serverUri, "__vite_ping");
        yield return _serverUri;
    }

    private ProcessStartInfo? CreatePnpmProcessStartInfo()
    {
        var baseArguments = "--filter @bugence/dashboard dev";
        var extraArguments = Environment.GetEnvironmentVariable(AdditionalArgsEnv);
        if (!string.IsNullOrWhiteSpace(extraArguments))
        {
            baseArguments = $"{baseArguments} {extraArguments}";
        }

        var overrideExecutable = Environment.GetEnvironmentVariable(PnpmExecutableOverrideEnv);
        if (!string.IsNullOrWhiteSpace(overrideExecutable))
        {
            var resolvedOverride = ResolveExecutablePath(overrideExecutable);
            if (resolvedOverride is not null)
            {
                return BuildStartInfo(resolvedOverride, baseArguments);
            }

            _logger.LogWarning(
                "The pnpm executable override at '{Executable}' could not be found. Falling back to auto-detection.",
                overrideExecutable);
        }

        foreach (var candidate in GetCommandCandidates())
        {
            var resolvedExecutable = ResolveExecutablePath(candidate.Executable);
            if (resolvedExecutable is null)
            {
                continue;
            }

            var arguments = string.IsNullOrWhiteSpace(candidate.ArgumentPrefix)
                ? baseArguments
                : $"{candidate.ArgumentPrefix} {baseArguments}";

            return BuildStartInfo(resolvedExecutable, arguments);
        }

        return null;
    }

    private ProcessStartInfo BuildStartInfo(string executablePath, string arguments)
    {
        return new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            WorkingDirectory = _workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
    }

    private string? ResolveExecutablePath(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        if (Path.IsPathRooted(executable))
        {
            return File.Exists(executable) ? executable : null;
        }

        var searchDirectories = new List<string>
        {
            _workingDirectory,
            Path.Combine(_workingDirectory, "node_modules", ".bin"),
            Path.Combine(_workingDirectory, "packages", "dashboard", "node_modules", ".bin")
        };

        var pathVariableName = OperatingSystem.IsWindows() ? "Path" : "PATH";
        var pathValue = Environment.GetEnvironmentVariable(pathVariableName);
        if (!string.IsNullOrEmpty(pathValue))
        {
            searchDirectories.AddRange(
                pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        foreach (var directory in searchDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (OperatingSystem.IsWindows() && Path.GetExtension(executable).Length == 0)
            {
                foreach (var extension in WindowsExecutableExtensions)
                {
                    var extendedCandidate = candidate + extension;
                    if (File.Exists(extendedCandidate))
                    {
                        return extendedCandidate;
                    }
                }
            }
        }

        return null;
    }

    private static IEnumerable<(string Executable, string ArgumentPrefix)> GetCommandCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return ("pnpm.cmd", string.Empty);
            yield return ("pnpm.exe", string.Empty);
            yield return ("pnpm", string.Empty);
            yield return ("corepack.cmd", "pnpm");
            yield return ("corepack.exe", "pnpm");
            yield return ("corepack", "pnpm");
            yield return ("npx.cmd", "--yes pnpm");
            yield return ("npx.exe", "--yes pnpm");
            yield return ("npx", "--yes pnpm");
        }
        else
        {
            yield return ("pnpm", string.Empty);
            yield return ("corepack", "pnpm");
            yield return ("npx", "--yes pnpm");
        }
    }
}
