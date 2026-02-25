using System.Collections.ObjectModel;

namespace BugenceEditConsole.Services;

/// <summary>
/// Central catalog describing which static asset backs each editable page and
/// the selector hints that help us resolve sections within the legacy markup.
/// </summary>
public static class EditorAssetCatalog
{
    private static readonly IReadOnlyDictionary<string, string> _pageAssets =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["index"] = "index.html",
                ["meet-pete-d"] = "MeetPeteD.html",
                ["book-pete-d"] = "BookPeteD.html",
                ["join-community"] = "JoinTheCommunity.html"
            });

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _selectorHints =
        new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["book-pete-d"] = new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["hero_title"] = "[data-bugence-section='hero_title']",
                        ["hero_pitch"] = "[data-bugence-section='hero_pitch']",
                        ["booking_cta"] = "[data-bugence-section='booking_cta']",
                        ["booking_visual"] = "[data-bugence-section='booking_visual']"
                    }),
                ["meet-pete-d"] = new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["hero_title"] = "#hero-desktop h1",
                        ["hero_bio"] = "#hero-desktop p",
                        ["hero_quote"] = "#hero-desktop blockquote, #hero-desktop q",
                        ["hero_portrait"] = "#hero-desktop img[alt*='Pete'], #hero-desktop img"
                    }),
                ["join-community"] = new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["community_visual"] = "[data-bugence-section='community_visual'], #hero-desktop header > div.relative > img",
                        ["hero_title"] = "#hero-desktop h1, #hero-desktop .hero-copy h1",
                        ["hero_subtitle"] = "#hero-desktop p, #hero-desktop .hero-copy p"
                    })
            });

    private static readonly IReadOnlyDictionary<string, string> _emptyHints =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyDictionary<string, string> PageAssets => _pageAssets;

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SelectorHints => _selectorHints;

    public static bool TryResolveAsset(string slug, out string assetFileName)
    {
        if (_pageAssets.TryGetValue(slug, out var fileName) && !string.IsNullOrWhiteSpace(fileName))
        {
            assetFileName = fileName;
            return true;
        }

        assetFileName = string.Empty;
        return false;
    }

    public static IReadOnlyDictionary<string, string> GetSelectorHints(string slug) =>
        _selectorHints.TryGetValue(slug, out var hints) ? hints : _emptyHints;

    public static IEnumerable<string> GetHintCandidates(string slug, string sectionKey)
    {
        var hints = GetSelectorHints(slug);
        if (!hints.TryGetValue(sectionKey, out var hintValue) || string.IsNullOrWhiteSpace(hintValue))
        {
            yield break;
        }

        foreach (var candidate in hintValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            yield return candidate;
        }
    }
}

