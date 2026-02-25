using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace BugenceEditConsole.Services;

public record StaticSiteState(
    string Name,
    string ActivePhysicalPath,
    string EntryPoint,
    DateTimeOffset UploadedAtUtc,
    long PackageSizeBytes,
    int FileCount);

public interface IStaticSiteManager
{
    StaticSiteState? GetActiveState();
    string? GetActiveSitePath();
    Task<StaticSiteState> UploadAsync(IFormFile archive, CancellationToken cancellationToken = default);
}

public class StaticSiteManager : IStaticSiteManager
{
    private const long MaxArchiveBytes = 250 * 1024 * 1024; // 250 MB
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<StaticSiteManager> _logger;
    private readonly string _siteRoot;
    private readonly string _stateFile;

    public StaticSiteManager(IWebHostEnvironment environment, ILogger<StaticSiteManager> logger)
    {
        _environment = environment;
        _logger = logger;
        _siteRoot = Path.Combine(environment.ContentRootPath, "SiteUploads");
        _stateFile = Path.Combine(_siteRoot, "active-site.json");
        Directory.CreateDirectory(_siteRoot);
    }

    public StaticSiteState? GetActiveState()
    {
        if (!File.Exists(_stateFile))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_stateFile);
            return JsonSerializer.Deserialize<StaticSiteState>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read active site state; falling back to disk scan.");
            return null;
        }
    }

    public string? GetActiveSitePath()
    {
        var state = GetActiveState();
        if (state is not null && Directory.Exists(state.ActivePhysicalPath))
        {
            return state.ActivePhysicalPath;
        }

        var activeDir = Path.Combine(_siteRoot, "active");
        return Directory.Exists(activeDir) ? activeDir : null;
    }

    public async Task<StaticSiteState> UploadAsync(IFormFile archive, CancellationToken cancellationToken = default)
    {
        if (archive is null || archive.Length == 0)
        {
            throw new InvalidOperationException("Archive is empty or missing.");
        }

        var extension = Path.GetExtension(archive.FileName).ToLowerInvariant();
        if (!string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only .zip archives are supported for static site uploads.");
        }

        if (archive.Length > MaxArchiveBytes)
        {
            throw new InvalidOperationException("Archive exceeds the 250 MB limit.");
        }

        var tempDir = Path.Combine(_siteRoot, "temp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "upload.zip");
        await using (var stream = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write))
        {
            await archive.CopyToAsync(stream, cancellationToken);
        }

        var extractRoot = Path.Combine(_siteRoot, "packages", DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);
        ZipFile.ExtractToDirectory(tempFile, extractRoot);

        var indexPath = Directory.GetFiles(extractRoot, "index.html", SearchOption.AllDirectories).FirstOrDefault();
        if (indexPath is null)
        {
            Directory.Delete(extractRoot, recursive: true);
            throw new InvalidOperationException("Archive must contain an index.html file.");
        }

        var siteRoot = Path.GetDirectoryName(indexPath)!;
        var stagingDir = Path.Combine(_siteRoot, "staging-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(siteRoot, stagingDir);

        var activeDir = Path.Combine(_siteRoot, "active");
        var retiredDir = Path.Combine(_siteRoot, "retired", DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
        Directory.CreateDirectory(Path.GetDirectoryName(retiredDir)!);

        if (Directory.Exists(activeDir))
        {
            try
            {
                Directory.Move(activeDir, retiredDir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to move existing active site to retired folder.");
                TryDelete(activeDir);
            }
        }

        Directory.Move(stagingDir, activeDir);

        var fileCount = Directory.Exists(activeDir)
            ? Directory.GetFiles(activeDir, "*", SearchOption.AllDirectories).Length
            : 0;

        var state = new StaticSiteState(
            Name: Path.GetFileNameWithoutExtension(archive.FileName),
            ActivePhysicalPath: activeDir,
            EntryPoint: "index.html",
            UploadedAtUtc: DateTimeOffset.UtcNow,
            PackageSizeBytes: archive.Length,
            FileCount: fileCount);

        PersistState(state);

        // Cleanup temp
        TryDelete(tempDir);

        return state;
    }

    private void PersistState(StaticSiteState state)
    {
        try
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist active site state.");
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var targetSubDir = Path.Combine(destinationDir, Path.GetFileName(directory));
            CopyDirectory(directory, targetSubDir);
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
