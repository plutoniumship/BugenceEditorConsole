using System.Net;
using System.Text.RegularExpressions;

namespace BugenceEditConsole.Models;

public static class PageSectionExtensions
{
    private static readonly Regex HtmlTagPattern = new(@"<\s*\/?\s*[a-zA-Z][\w:-]*[^>]*>", RegexOptions.Compiled);

    public static string GetEditorMode(this PageSection section) =>
        section.ContentType switch
        {
            SectionContentType.Text => "text",
            SectionContentType.RichText => "richtext",
            SectionContentType.Html => "html",
            _ => "text"
        };

    public static string ToEditorHtml(this PageSection section)
    {
        if (section is null)
        {
            return string.Empty;
        }

        return section.ContentType switch
        {
            SectionContentType.Text => WebUtility.HtmlEncode(section.ContentValue ?? string.Empty).Replace("\n", "<br />"),
            SectionContentType.RichText or SectionContentType.Html => section.ContentValue ?? string.Empty,
            _ => string.Empty
        };
    }

    public static string GetDisplayTitle(this PageSection section) =>
        section is null
            ? string.Empty
            : string.IsNullOrWhiteSpace(section.Title)
                ? section.SectionKey
                : section.Title!;

    public static string GetSummarySnippet(this PageSection section, int maxLength = 80)
    {
        if (section is null)
        {
            return string.Empty;
        }

        var source = section.ContentType == SectionContentType.Image
            ? section.MediaAltText ?? string.Empty
            : section.ContentValue ?? string.Empty;

        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var decoded = WebUtility.HtmlDecode(source);
        var plain = Regex.Replace(decoded, "<.*?>", string.Empty);
        var normalized = string.Join(" ", plain.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();

        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        var slice = normalized[..Math.Min(normalized.Length, maxLength)].TrimEnd();
        return $"{slice}...";
    }

    public static bool LooksLikeMarkup(this PageSection section)
    {
        if (section is null || string.IsNullOrWhiteSpace(section.ContentValue))
        {
            return false;
        }

        if (section.ContentType is SectionContentType.Html or SectionContentType.RichText)
        {
            return true;
        }

        return HtmlTagPattern.IsMatch(section.ContentValue);
    }
}
