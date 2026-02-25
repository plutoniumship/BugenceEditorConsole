using System.Security.Cryptography;
using System.Text.Json;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public sealed class SnapshotManifestEntry
{
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class SnapshotDiffResult
{
    public List<SnapshotManifestEntry> Added { get; set; } = new();
    public List<SnapshotManifestEntry> Removed { get; set; } = new();
    public List<(SnapshotManifestEntry From, SnapshotManifestEntry To)> Changed { get; set; } = new();
}

public sealed class SnapshotCreateResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public ProjectDeploySnapshot? Snapshot { get; set; }
}

public interface IProjectSnapshotService
{
    Task<SnapshotCreateResult> CreateSnapshotAsync(
        UploadedProject project,
        string environment,
        string source,
        string? createdByUserId,
        bool isSuccessful = true,
        string? versionLabel = null,
        CancellationToken cancellationToken = default);

    Task<(bool Success, string Message)> RestoreSnapshotAsync(
        UploadedProject project,
        ProjectDeploySnapshot snapshot,
        CancellationToken cancellationToken = default);

    IReadOnlyList<SnapshotManifestEntry> ParseManifest(string? manifestJson);
    SnapshotDiffResult Diff(IReadOnlyList<SnapshotManifestEntry> from, IReadOnlyList<SnapshotManifestEntry> to);
}

public sealed class ProjectSnapshotService : IProjectSnapshotService
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ProjectSnapshotService> _logger;

    public ProjectSnapshotService(ApplicationDbContext db, IWebHostEnvironment env, ILogger<ProjectSnapshotService> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    public async Task<SnapshotCreateResult> CreateSnapshotAsync(
        UploadedProject project,
        string environment,
        string source,
        string? createdByUserId,
        bool isSuccessful = true,
        string? versionLabel = null,
        CancellationToken cancellationToken = default)
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var projectRoot = Path.Combine(webRoot, "Uploads", project.FolderName);
        if (!Directory.Exists(projectRoot))
        {
            return new SnapshotCreateResult { Success = false, Message = "Project root does not exist." };
        }

        var snapshotId = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N")[..8];
        var snapshotsRoot = Path.Combine(projectRoot, ".bugence", "snapshots");
        Directory.CreateDirectory(snapshotsRoot);
        var snapshotRoot = Path.Combine(snapshotsRoot, snapshotId);
        Directory.CreateDirectory(snapshotRoot);

        CopyDirectory(projectRoot, snapshotRoot, skipBugenceSnapshotFolder: true);
        var manifest = BuildManifest(snapshotRoot);
        var relativeRoot = Path.GetRelativePath(webRoot, snapshotRoot).Replace("\\", "/");

        var snapshot = new ProjectDeploySnapshot
        {
            UploadedProjectId = project.Id,
            Environment = (environment ?? "draft").Trim().ToLowerInvariant(),
            VersionLabel = versionLabel,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = createdByUserId,
            ManifestJson = JsonSerializer.Serialize(manifest),
            RootPath = relativeRoot,
            Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source.Trim(),
            IsSuccessful = isSuccessful
        };

        _db.ProjectDeploySnapshots.Add(snapshot);
        await _db.SaveChangesAsync(cancellationToken);
        await PruneSnapshotsAsync(project.Id, 50, cancellationToken);

        await UpsertEnvironmentPointerAsync(project.Id, snapshot, cancellationToken);

        return new SnapshotCreateResult
        {
            Success = true,
            Message = "Snapshot created.",
            Snapshot = snapshot
        };
    }

    public async Task<(bool Success, string Message)> RestoreSnapshotAsync(
        UploadedProject project,
        ProjectDeploySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var projectRoot = Path.Combine(webRoot, "Uploads", project.FolderName);
        var snapshotRoot = Path.Combine(webRoot, (snapshot.RootPath ?? string.Empty).Replace("/", Path.DirectorySeparatorChar.ToString()));
        if (!Directory.Exists(snapshotRoot))
        {
            return (false, "Snapshot storage not found.");
        }

        var tempRestore = Path.Combine(webRoot, "Uploads", ".restore-temp", $"{project.Id}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRestore);
        try
        {
            CopyDirectory(snapshotRoot, tempRestore, skipBugenceSnapshotFolder: false);
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
            Directory.CreateDirectory(projectRoot);
            CopyDirectory(tempRestore, projectRoot, skipBugenceSnapshotFolder: false);
            return (true, "Restored.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot restore failed for project {ProjectId} snapshot {SnapshotId}", project.Id, snapshot.Id);
            return (false, "Restore failed.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRestore))
                {
                    Directory.Delete(tempRestore, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    public IReadOnlyList<SnapshotManifestEntry> ParseManifest(string? manifestJson)
    {
        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            return Array.Empty<SnapshotManifestEntry>();
        }
        try
        {
            var parsed = JsonSerializer.Deserialize<List<SnapshotManifestEntry>>(manifestJson);
            return parsed ?? new List<SnapshotManifestEntry>();
        }
        catch
        {
            return new List<SnapshotManifestEntry>();
        }
    }

    public SnapshotDiffResult Diff(IReadOnlyList<SnapshotManifestEntry> from, IReadOnlyList<SnapshotManifestEntry> to)
    {
        var result = new SnapshotDiffResult();
        var left = from.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var right = to.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);

        foreach (var key in right.Keys)
        {
            if (!left.TryGetValue(key, out var oldEntry))
            {
                result.Added.Add(right[key]);
                continue;
            }

            var next = right[key];
            if (!string.Equals(oldEntry.Sha256, next.Sha256, StringComparison.OrdinalIgnoreCase) || oldEntry.SizeBytes != next.SizeBytes)
            {
                result.Changed.Add((oldEntry, next));
            }
        }

        foreach (var key in left.Keys)
        {
            if (!right.ContainsKey(key))
            {
                result.Removed.Add(left[key]);
            }
        }

        return result;
    }

    private async Task UpsertEnvironmentPointerAsync(int projectId, ProjectDeploySnapshot snapshot, CancellationToken cancellationToken)
    {
        var pointer = await _db.ProjectEnvironmentPointers.FirstOrDefaultAsync(p => p.UploadedProjectId == projectId, cancellationToken);
        if (pointer == null)
        {
            pointer = new ProjectEnvironmentPointer
            {
                UploadedProjectId = projectId
            };
            _db.ProjectEnvironmentPointers.Add(pointer);
        }

        pointer.UpdatedAtUtc = DateTime.UtcNow;
        switch ((snapshot.Environment ?? "draft").Trim().ToLowerInvariant())
        {
            case "live":
                pointer.LiveSnapshotId = snapshot.Id;
                break;
            case "staging":
                pointer.StagingSnapshotId = snapshot.Id;
                break;
            default:
                pointer.DraftSnapshotId = snapshot.Id;
                break;
        }
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task PruneSnapshotsAsync(int projectId, int keep, CancellationToken cancellationToken)
    {
        var snapshots = await _db.ProjectDeploySnapshots
            .Where(s => s.UploadedProjectId == projectId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (snapshots.Count <= keep)
        {
            return;
        }

        var toDelete = snapshots.Skip(keep).ToList();
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        foreach (var snapshot in toDelete)
        {
            try
            {
                var full = Path.Combine(webRoot, (snapshot.RootPath ?? string.Empty).Replace("/", Path.DirectorySeparatorChar.ToString()));
                if (Directory.Exists(full))
                {
                    Directory.Delete(full, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed deleting snapshot folder for snapshot {SnapshotId}", snapshot.Id);
            }
        }

        _db.ProjectDeploySnapshots.RemoveRange(toDelete);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static List<SnapshotManifestEntry> BuildManifest(string root)
    {
        var list = new List<SnapshotManifestEntry>();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, file).Replace("\\", "/");
            using var stream = File.OpenRead(file);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            list.Add(new SnapshotManifestEntry
            {
                Path = rel,
                SizeBytes = new FileInfo(file).Length,
                Sha256 = Convert.ToHexString(hash)
            });
        }
        return list.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void CopyDirectory(string sourceDir, string destinationDir, bool skipBugenceSnapshotFolder)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDir))
        {
            var name = Path.GetFileName(directory);
            if (string.Equals(name, ".bugence", StringComparison.OrdinalIgnoreCase) && skipBugenceSnapshotFolder)
            {
                continue;
            }

            var nextDest = Path.Combine(destinationDir, name);
            Directory.CreateDirectory(nextDest);
            CopyDirectory(directory, nextDest, skipBugenceSnapshotFolder);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            var dest = Path.Combine(destinationDir, name);
            File.Copy(file, dest, overwrite: true);
        }
    }
}
