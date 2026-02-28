using System.Text.Json;
using System.Text.RegularExpressions;

namespace BugenceEditConsole.Infrastructure;

public sealed class ProjectPageRouteAlias
{
    public string SourcePath { get; set; } = string.Empty;
    public string RoutePath { get; set; } = string.Empty;
}

public static class ProjectRoutingSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool IsHtmlPage(string? path)
    {
        var normalized = NormalizeFilePath(path);
        return normalized.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith('/'))
        {
            normalized = normalized[1..];
        }

        return normalized;
    }

    public static string NormalizeRoutePath(string? routePath)
    {
        if (string.IsNullOrWhiteSpace(routePath))
        {
            return string.Empty;
        }

        var normalized = routePath.Replace('\\', '/').Trim();
        normalized = Regex.Replace(normalized, "\\s+", "-");
        normalized = normalized.Trim('/');

        if (normalized.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^5];
        }
        else if (normalized.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized.Trim('/');
    }

    public static IReadOnlyDictionary<string, string> ParseAliases(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var items = JsonSerializer.Deserialize<List<ProjectPageRouteAlias>>(json, JsonOptions) ?? new List<ProjectPageRouteAlias>();
            return ToDictionary(items);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static string SerializeAliases(IEnumerable<ProjectPageRouteAlias> aliases)
    {
        var normalized = aliases
            .Select(alias => new ProjectPageRouteAlias
            {
                SourcePath = NormalizeFilePath(alias.SourcePath),
                RoutePath = NormalizeRoutePath(alias.RoutePath)
            })
            .Where(alias => !string.IsNullOrWhiteSpace(alias.SourcePath) && !string.IsNullOrWhiteSpace(alias.RoutePath))
            .GroupBy(alias => alias.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(alias => alias.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.Count == 0
            ? string.Empty
            : JsonSerializer.Serialize(normalized, JsonOptions);
    }

    public static string? ResolveIncomingPath(
        string? requestPath,
        string? landingPagePath,
        IReadOnlyDictionary<string, string> aliases)
    {
        var normalizedRequest = NormalizeRoutePath(requestPath);
        if (string.IsNullOrWhiteSpace(normalizedRequest))
        {
            return NormalizeFilePath(landingPagePath);
        }

        foreach (var pair in aliases)
        {
            if (string.Equals(NormalizeRoutePath(pair.Value), normalizedRequest, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeFilePath(pair.Key);
            }
        }

        return null;
    }

    public static IReadOnlyDictionary<string, string> ToDictionary(IEnumerable<ProjectPageRouteAlias> aliases)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in aliases)
        {
            var source = NormalizeFilePath(alias.SourcePath);
            var route = NormalizeRoutePath(alias.RoutePath);
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(route))
            {
                continue;
            }

            map[source] = route;
        }

        return map;
    }
}
