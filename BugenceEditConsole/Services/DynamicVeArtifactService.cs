using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public interface IDynamicVeArtifactService
{
    Task<(string ArtifactPath, string Checksum, string Json)> BuildOverlayArtifactAsync(
        UploadedProject project,
        DynamicVePageRevision revision,
        ApplicationDbContext db,
        CancellationToken cancellationToken = default);
}

public sealed class DynamicVeArtifactService : IDynamicVeArtifactService
{
    private readonly IWebHostEnvironment _env;

    public DynamicVeArtifactService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task<(string ArtifactPath, string Checksum, string Json)> BuildOverlayArtifactAsync(
        UploadedProject project,
        DynamicVePageRevision revision,
        ApplicationDbContext db,
        CancellationToken cancellationToken = default)
    {
        var rules = await db.DynamicVePatchRules
            .AsNoTracking()
            .Where(x => x.RevisionId == revision.Id)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var texts = await db.DynamicVeTextPatches
            .AsNoTracking()
            .Where(x => x.RevisionId == revision.Id)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var sections = await db.DynamicVeSectionInstances
            .AsNoTracking()
            .Where(x => x.RevisionId == revision.Id)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var bindings = await db.DynamicVeActionBindings
            .AsNoTracking()
            .Where(x => x.RevisionId == revision.Id)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var maps = await db.DynamicVeElementMaps
            .AsNoTracking()
            .Where(x => x.RevisionId == revision.Id)
            .ToListAsync(cancellationToken);
        var mapByKey = maps.ToDictionary(x => x.ElementKey, StringComparer.OrdinalIgnoreCase);

        static IReadOnlyList<string> ParseFallbacks(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
            try
            {
                return JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        string MapSelector(string key)
            => mapByKey.TryGetValue(key, out var map) ? map.PrimarySelector : string.Empty;

        IReadOnlyList<string> MapFallbacks(string key)
            => mapByKey.TryGetValue(key, out var map) ? ParseFallbacks(map.FallbackSelectorsJson) : Array.Empty<string>();

        var payload = new
        {
            revisionId = revision.Id,
            pagePath = revision.PagePath,
            generatedAtUtc = DateTime.UtcNow,
            elementMaps = maps.Select(x => new
            {
                x.ElementKey,
                primarySelector = x.PrimarySelector,
                fallbackSelectors = ParseFallbacks(x.FallbackSelectorsJson),
                x.FingerprintHash,
                x.AnchorHash,
                x.Confidence,
                x.LastResolvedSelector,
                x.LastResolvedAtUtc
            }),
            rules = rules.Select(x => new
            {
                x.Id,
                x.ElementKey,
                selector = MapSelector(x.ElementKey),
                fallbackSelectors = MapFallbacks(x.ElementKey),
                x.RuleType,
                x.Breakpoint,
                x.State,
                x.Property,
                x.Value,
                x.Priority
            }),
            textPatches = texts.Select(x => new
            {
                x.Id,
                x.ElementKey,
                selector = MapSelector(x.ElementKey),
                fallbackSelectors = MapFallbacks(x.ElementKey),
                x.TextMode,
                x.Content
            }),
            sectionInstances = sections.Select(x => new
            {
                x.Id,
                x.TemplateId,
                x.InsertMode,
                x.TargetElementKey,
                selector = MapSelector(x.TargetElementKey ?? string.Empty),
                fallbackSelectors = MapFallbacks(x.TargetElementKey ?? string.Empty),
                x.MarkupJson,
                x.CssJson,
                x.JsJson
            }),
            actionBindings = bindings.Select(x => new
            {
                x.Id,
                x.ElementKey,
                selector = MapSelector(x.ElementKey),
                fallbackSelectors = MapFallbacks(x.ElementKey),
                x.ActionType,
                x.WorkflowId,
                x.NavigateUrl,
                x.BehaviorJson
            })
        };

        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        var checksum = Convert.ToHexString(SHA256.HashData(bytes));

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var folder = Path.Combine(webRoot, "Uploads", project.FolderName, ".bugence", "dve");
        Directory.CreateDirectory(folder);
        var fileName = $"overlay-{revision.Id}.json";
        var fullPath = Path.Combine(folder, fileName);
        await File.WriteAllTextAsync(fullPath, json, Encoding.UTF8, cancellationToken);
        var relative = Path.GetRelativePath(webRoot, fullPath).Replace("\\", "/");
        return ("/" + relative, checksum, json);
    }
}
