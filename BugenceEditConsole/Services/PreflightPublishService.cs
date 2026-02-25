using System.Net;
using System.Text.RegularExpressions;
using BugenceEditConsole.Models;

namespace BugenceEditConsole.Services;

public sealed class PreflightPublishResult
{
    public bool Safe { get; set; } = true;
    public int Score { get; set; } = 100;
    public List<string> Blockers { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> ChangedAbsolute { get; set; } = new();
    public object DiffSummary { get; set; } = new { added = 0, removed = 0, changed = 0 };
}

public sealed class PreflightPublishRequest
{
    public UploadedProject Project { get; set; } = default!;
    public string FilePath { get; set; } = "index.html";
    public string HtmlBefore { get; set; } = string.Empty;
    public string HtmlAfter { get; set; } = string.Empty;
    public string WebRootPath { get; set; } = string.Empty;
    public string ProjectRootPath { get; set; } = string.Empty;
}

public interface IPreflightPublishService
{
    PreflightPublishResult Evaluate(PreflightPublishRequest request);
}

public sealed class PreflightPublishService : IPreflightPublishService
{
    public PreflightPublishResult Evaluate(PreflightPublishRequest request)
    {
        var result = new PreflightPublishResult();
        var before = NormalizeHtml(request.HtmlBefore);
        var after = NormalizeHtml(request.HtmlAfter);

        if (!LooksLikeHtml(after))
        {
            result.Blockers.Add("Invalid HTML payload (missing <html> or <body>).");
        }

        var beforeCss = CollectStylesheetRefs(before);
        var afterCss = CollectStylesheetRefs(after);
        var beforeJs = CollectScriptRefs(before);
        var afterJs = CollectScriptRefs(after);

        var removedCss = beforeCss.Where(x => !afterCss.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
        var removedJs = beforeJs.Where(x => !afterJs.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
        if (removedCss.Count > 0)
        {
            result.Blockers.Add($"Removed stylesheet references: {string.Join(", ", removedCss.Take(6))}");
        }
        if (removedJs.Count > 0)
        {
            result.Blockers.Add($"Removed script references: {string.Join(", ", removedJs.Take(6))}");
        }

        var beforeMain = Regex.Matches(before, "<main\\b", RegexOptions.IgnoreCase).Count;
        var afterMain = Regex.Matches(after, "<main\\b", RegexOptions.IgnoreCase).Count;
        var beforeSection = Regex.Matches(before, "<section\\b", RegexOptions.IgnoreCase).Count;
        var afterSection = Regex.Matches(after, "<section\\b", RegexOptions.IgnoreCase).Count;
        if (beforeMain > 0 && afterMain == 0)
        {
            result.Blockers.Add("Main container was removed.");
        }
        if (beforeSection > 0 && afterSection == 0)
        {
            result.Blockers.Add("All section containers were removed.");
        }
        else if (beforeSection > 0 && afterSection < Math.Max(1, (int)Math.Floor(beforeSection * 0.3)))
        {
            result.Warnings.Add("Large section count reduction detected.");
        }

        if (Regex.IsMatch(after, "<script[^>]*id=[\"']bugence-workflow-runner[\"'][^>]*>", RegexOptions.IgnoreCase))
        {
            if (!Regex.IsMatch(after, "<script[^>]*id=[\"']bugence-workflow-runner[\"'][^>]*src=[\"']/js/workflow-trigger-runner\\.js[\"']", RegexOptions.IgnoreCase))
            {
                result.Warnings.Add("Workflow runner script does not reference /js/workflow-trigger-runner.js.");
            }
        }

        var beforeAbs = CollectAbsoluteRefs(before);
        var afterAbs = CollectAbsoluteRefs(after);
        result.ChangedAbsolute = beforeAbs.Where(x => !afterAbs.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
        if (result.ChangedAbsolute.Count > 0)
        {
            result.Warnings.Add($"Absolute references changed: {result.ChangedAbsolute.Count}.");
        }

        var missingAssets = CountMissingAssets(after, request.ProjectRootPath, request.WebRootPath);
        if (missingAssets > 0)
        {
            result.Warnings.Add($"Missing assets detected: {missingAssets}.");
        }

        var addedCount = afterAbs.Count(x => !beforeAbs.Contains(x, StringComparer.OrdinalIgnoreCase));
        var removedCount = result.ChangedAbsolute.Count;
        var changedCount = removedCss.Count + removedJs.Count;
        result.DiffSummary = new { added = addedCount, removed = removedCount, changed = changedCount };

        var score = 100;
        score -= result.Blockers.Count * 35;
        score -= result.Warnings.Count * 8;
        result.Score = Math.Max(0, Math.Min(100, score));
        result.Safe = result.Blockers.Count == 0;
        return result;
    }

    private static string NormalizeHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var output = html;
        output = Regex.Replace(output, "<base\\b[^>]*data-bugence-preview=[\"'][^\"']*[\"'][^>]*>", string.Empty, RegexOptions.IgnoreCase);
        output = Regex.Replace(output, "<base\\b[^>]*href\\s*=\\s*([\"'])/Editor\\?handler=Preview[^\"']*\\1[^>]*>", string.Empty, RegexOptions.IgnoreCase);
        output = Regex.Replace(output, "<[^>]*data-bugence-editor=[\"']true[\"'][^>]*>.*?</[^>]+>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        output = output.Replace("&amp;", "&");
        return output;
    }

    private static bool LooksLikeHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return false;
        return Regex.IsMatch(html, "<html\\b", RegexOptions.IgnoreCase) && Regex.IsMatch(html, "<body\\b", RegexOptions.IgnoreCase);
    }

    private static List<string> CollectStylesheetRefs(string html)
    {
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, "<link\\b[^>]*href\\s*=\\s*(['\"])(.*?)\\1[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var tag = m.Value;
            var rel = Regex.Match(tag, "rel\\s*=\\s*(['\"])(.*?)\\1", RegexOptions.IgnoreCase);
            if (!rel.Success || !rel.Groups[2].Value.Contains("stylesheet", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var value = CanonicalizeAssetRef(WebUtility.HtmlDecode(m.Groups[2].Value).Trim());
            if (IsSkippable(value)) continue;
            refs.Add(value);
        }
        return refs.ToList();
    }

    private static List<string> CollectScriptRefs(string html)
    {
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, "<script\\b[^>]*src\\s*=\\s*(['\"])(.*?)\\1[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var value = CanonicalizeAssetRef(WebUtility.HtmlDecode(m.Groups[2].Value).Trim());
            if (IsSkippable(value)) continue;
            refs.Add(value);
        }
        return refs.ToList();
    }

    private static List<string> CollectAbsoluteRefs(string html)
    {
        var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, "\\b(?:src|href|poster)\\s*=\\s*(['\"])(.*?)\\1", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var value = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
            if (!value.StartsWith("/", StringComparison.Ordinal)) continue;
            if (value.StartsWith("/Editor?", StringComparison.OrdinalIgnoreCase)) continue;
            refs.Add(value);
        }
        return refs.ToList();
    }

    private static int CountMissingAssets(string html, string projectRoot, string webRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            return 0;
        }

        var refs = CollectAbsoluteRefs(html);
        var missing = 0;
        foreach (var value in refs)
        {
            var path = value.Split('?', '#')[0];
            if (string.IsNullOrWhiteSpace(path)) continue;
            var clean = path.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString());
            var fullWeb = Path.Combine(webRoot, clean);
            var fullProject = Path.Combine(projectRoot, clean);
            if (File.Exists(fullWeb) || File.Exists(fullProject))
            {
                continue;
            }
            missing++;
        }
        return missing;
    }

    private static bool IsSkippable(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return value.StartsWith("#", StringComparison.Ordinal)
               || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith("//", StringComparison.OrdinalIgnoreCase);
    }

    private static string CanonicalizeAssetRef(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Trim().Replace("\\", "/");
        if (normalized.StartsWith("/Editor?", StringComparison.OrdinalIgnoreCase))
        {
            var queryIndex = normalized.IndexOf('?');
            if (queryIndex >= 0)
            {
                var query = normalized[(queryIndex + 1)..].Replace("&amp;", "&");
                var parameters = ParseQueryParams(query);
                var handler = parameters.TryGetValue("handler", out var h) ? h : string.Empty;
                var lookup = handler.Equals("asset", StringComparison.OrdinalIgnoreCase) ? "path"
                    : handler.Equals("preview", StringComparison.OrdinalIgnoreCase) ? "file"
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(lookup) && parameters.TryGetValue(lookup, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
                {
                    normalized = mapped;
                }
            }
        }

        normalized = WebUtility.UrlDecode(normalized).Replace("\\", "/");
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }
        normalized = normalized.TrimStart('/');
        return normalized;
    }

    private static Dictionary<string, string> ParseQueryParams(string query)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query)) return map;
        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pair in pairs)
        {
            var idx = pair.IndexOf('=');
            if (idx < 0)
            {
                map[WebUtility.UrlDecode(pair)] = string.Empty;
                continue;
            }
            var key = WebUtility.UrlDecode(pair[..idx]);
            var val = WebUtility.UrlDecode(pair[(idx + 1)..]);
            map[key] = val;
        }
        return map;
    }
}
