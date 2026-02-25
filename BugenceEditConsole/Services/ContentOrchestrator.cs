using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace BugenceEditConsole.Services;

public class ContentOrchestrator : IContentOrchestrator
{
    private readonly ApplicationDbContext _db;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<ContentOrchestrator> _logger;
    private readonly IWebHostEnvironment _environment;

    public ContentOrchestrator(
        ApplicationDbContext db,
        IFileStorageService fileStorage,
        ILogger<ContentOrchestrator> logger,
        IWebHostEnvironment environment)
    {
        _db = db;
        _fileStorage = fileStorage;
        _logger = logger;
        _environment = environment;
    }

    public async Task<IReadOnlyList<SitePage>> GetPagesAsync(CancellationToken cancellationToken = default) =>
        await _db.SitePages
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

    public async Task<SitePage?> GetPageWithSectionsAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _db.SitePages
                .AsNoTracking()
                .Include(p => p.Sections.OrderBy(s => s.DisplayOrder))
                .FirstOrDefaultAsync(p => p.Id == pageId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("GetPageWithSectionsAsync cancelled for PageId {PageId}", pageId);
            return null;
        }
    }

    public async Task<PageSection?> GetSectionByIdAsync(Guid sectionId, CancellationToken cancellationToken = default) =>
        await _db.PageSections
            .Include(s => s.SitePage)
            .FirstOrDefaultAsync(s => s.Id == sectionId, cancellationToken);

    public async Task<PageSection?> GetSectionBySelectorAsync(Guid pageId, string selector, CancellationToken cancellationToken = default) =>
        await _db.PageSections
            .Include(s => s.SitePage)
            .FirstOrDefaultAsync(s => s.SitePageId == pageId && s.CssSelector == selector, cancellationToken);

    public async Task<ContentChangeResult> UpdateSectionAsync(
        Guid sectionId,
        string? newContent,
        string? mediaAltText,
        IFormFile? image,
        string userId,
        string userDisplayName,
        CancellationToken cancellationToken = default)
    {
        var section = await _db.PageSections
            .Include(s => s.SitePage)
            .FirstOrDefaultAsync(s => s.Id == sectionId, cancellationToken);

        if (section is null)
        {
            return new ContentChangeResult(false, "Section not found.", null, null);
        }

        if (section.IsLocked)
        {
            return new ContentChangeResult(false, "Section is locked and cannot be edited.", section, null);
        }

        var oldValue = section.ContentValue;
        var oldMediaPath = section.MediaPath;

        if (section.ContentType is SectionContentType.Text or SectionContentType.Html or SectionContentType.RichText)
        {
            section.ContentValue = newContent;
        }

        if (image is not null)
        {
            if (!string.IsNullOrWhiteSpace(section.MediaPath))
            {
                await _fileStorage.DeleteAsync(section.MediaPath, cancellationToken);
            }

            section.MediaPath = await _fileStorage.SaveAsync(image, section.SitePage.Slug, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(mediaAltText))
        {
            section.MediaAltText = mediaAltText;
        }

        section.UpdatedAtUtc = DateTime.UtcNow;
        section.SitePage.Touch();

        var changeSummary = BuildChangeSummary(section, newContent, oldValue, oldMediaPath, section.MediaPath);

        var log = new ContentChangeLog
        {
            SitePageId = section.SitePageId,
            PageSectionId = section.Id,
            FieldKey = section.SectionKey,
            PreviousValue = Truncate(oldValue),
            NewValue = Truncate(section.ContentValue),
            ChangeSummary = changeSummary,
            PerformedByUserId = userId,
            PerformedByDisplayName = userDisplayName,
            PerformedAtUtc = DateTime.UtcNow
        };

        _db.ContentChangeLogs.Add(log);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {User} updated section {SectionKey} on {Page}", userDisplayName, section.SectionKey, section.SitePage.Name);
        return new ContentChangeResult(true, "Section updated successfully.", section, log);
    }

    public async Task<ContentChangeResult> UpsertSectionAsync(
        Guid pageId,
        Guid? sectionId,
        string? selector,
        SectionContentType contentType,
        string? newContent,
        string? mediaAltText,
        IFormFile? image,
        string userId,
        string userDisplayName,
        CancellationToken cancellationToken = default)
    {
        var page = await _db.SitePages
            .Include(p => p.Sections)
            .FirstOrDefaultAsync(p => p.Id == pageId, cancellationToken);

        if (page is null)
        {
            return new ContentChangeResult(false, "Page not found.", null, null);
        }

        PageSection? section = null;

        if (sectionId.HasValue && sectionId.Value != Guid.Empty)
        {
            section = await _db.PageSections
                .Include(s => s.SitePage)
                .FirstOrDefaultAsync(s => s.Id == sectionId.Value && s.SitePageId == pageId, cancellationToken);
        }

        if (section is null && !string.IsNullOrWhiteSpace(selector))
        {
            section = await _db.PageSections
                .Include(s => s.SitePage)
                .FirstOrDefaultAsync(s => s.SitePageId == pageId && s.CssSelector == selector, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var isNew = section is null;
        string? oldValue = null;
        string? oldMediaPath = null;

        if (section is null)
        {
            if (string.IsNullOrWhiteSpace(selector))
            {
                return new ContentChangeResult(false, "Selector is required for new sections.", null, null);
            }

            section = new PageSection
            {
                Id = Guid.NewGuid(),
                SitePageId = pageId,
                SitePage = page,
                SectionKey = GenerateSectionKey(page.Slug),
                Title = null,
                ContentType = contentType,
                ContentValue = contentType is SectionContentType.Text or SectionContentType.RichText or SectionContentType.Html
                    ? newContent
                    : null,
                MediaAltText = mediaAltText,
                CssSelector = selector,
                DisplayOrder = page.Sections.Any() ? page.Sections.Max(s => s.DisplayOrder) + 1 : 0,
                IsLocked = false,
                UpdatedAtUtc = now
            };

            if (contentType == SectionContentType.Image && image is not null)
            {
                section.MediaPath = await _fileStorage.SaveAsync(image, page.Slug, cancellationToken);
            }

            page.Sections.Add(section);
            _db.PageSections.Add(section);
        }
        else
        {
            oldValue = section.ContentValue;
            oldMediaPath = section.MediaPath;

            if (!string.IsNullOrWhiteSpace(selector))
            {
                section.CssSelector = selector;
            }

            if (section.ContentType != contentType)
            {
                section.ContentType = contentType;
            }

            if (section.ContentType is SectionContentType.Text or SectionContentType.RichText or SectionContentType.Html)
            {
                section.ContentValue = newContent;
            }

            if (image is not null)
            {
                if (!string.IsNullOrWhiteSpace(section.MediaPath))
                {
                    await _fileStorage.DeleteAsync(section.MediaPath, cancellationToken);
                }

                section.MediaPath = await _fileStorage.SaveAsync(image, section.SitePage.Slug, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(mediaAltText))
            {
                section.MediaAltText = mediaAltText;
            }

            section.UpdatedAtUtc = now;
        }

        page.Touch();

        var changeSummary = isNew
            ? "Section created via visual editor"
            : BuildChangeSummary(section, newContent, oldValue, oldMediaPath, section.MediaPath);

        var log = new ContentChangeLog
        {
            SitePageId = page.Id,
            PageSectionId = section.Id,
            FieldKey = section.SectionKey,
            PreviousValue = Truncate(oldValue),
            NewValue = Truncate(section.ContentValue),
            ChangeSummary = changeSummary,
            PerformedByUserId = userId,
            PerformedByDisplayName = userDisplayName,
            PerformedAtUtc = now
        };

        _db.ContentChangeLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {User} upserted section {SectionKey} on {Page}", userDisplayName, section.SectionKey, page.Name);

        return new ContentChangeResult(true, isNew ? "Section created." : "Section updated.", section, log);
    }

    public async Task<ContentChangeResult> DeleteSectionAsync(
        Guid pageId,
        Guid sectionId,
        string userId,
        string userDisplayName,
        CancellationToken cancellationToken = default)
    {
        var section = await _db.PageSections
            .Include(s => s.SitePage)
            .FirstOrDefaultAsync(s => s.Id == sectionId && s.SitePageId == pageId, cancellationToken);

        if (section is null)
        {
            return new ContentChangeResult(false, "Section not found.", null, null);
        }

        if (section.IsLocked)
        {
            return new ContentChangeResult(false, "Section is locked and cannot be deleted.", section, null);
        }

        var hadContent = !string.IsNullOrWhiteSpace(section.ContentValue) || !string.IsNullOrWhiteSpace(section.MediaPath);
        if (!hadContent)
        {
            return new ContentChangeResult(true, "Section already cleared.", section, null);
        }

        var previousValue = section.ContentValue;
        var previousMedia = section.MediaPath;

        if (!string.IsNullOrWhiteSpace(previousMedia))
        {
            await _fileStorage.DeleteAsync(previousMedia, cancellationToken);
        }

        section.ContentValue = null;
        section.MediaPath = null;
        section.MediaAltText = null;
        section.UpdatedAtUtc = DateTime.UtcNow;
        section.SitePage.Touch();

        var log = new ContentChangeLog
        {
            SitePageId = section.SitePageId,
            PageSectionId = section.Id,
            FieldKey = section.SectionKey,
            PreviousValue = Truncate(previousValue),
            NewValue = null,
            ChangeSummary = "Section cleared via visual editor",
            PerformedByUserId = userId,
            PerformedByDisplayName = userDisplayName,
            PerformedAtUtc = DateTime.UtcNow
        };

        _db.ContentChangeLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {User} removed section content for {SectionKey} on {Page}", userDisplayName, section.SectionKey, section.SitePage.Name);
        return new ContentChangeResult(true, "Section content removed.", section, log);
    }

    private static string GenerateSectionKey(string slug) =>
        $"auto_{slug}_{Guid.NewGuid():N}";

    public async Task SyncPageFromAssetAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Sync aborted before start for page {PageId}", pageId);
                return;
            }

            var page = await _db.SitePages
                .Include(p => p.Sections)
                .AsSplitQuery()
                .FirstOrDefaultAsync(p => p.Id == pageId, cancellationToken)
                .ConfigureAwait(false);

            if (page is null || page.Sections.Count == 0)
            {
                return;
            }

            if (!EditorAssetCatalog.TryResolveAsset(page.Slug, out var assetFile))
            {
                _logger.LogWarning("Sync skipped for {Slug}: asset mapping missing.", page.Slug);
                return;
            }

            var legacySiteRoot = Path.GetFullPath(Path.Combine(_environment.ContentRootPath ?? string.Empty, ".."));
            var assetPath = Path.Combine(legacySiteRoot, assetFile);
            if (!File.Exists(assetPath))
            {
                _logger.LogWarning("Sync skipped for {Slug}: asset {Asset} not found.", page.Slug, assetPath);
                return;
            }

            var html = await File.ReadAllTextAsync(assetPath, cancellationToken).ConfigureAwait(false);
            var parser = new HtmlParser();
            var document = await parser.ParseDocumentAsync(html, cancellationToken).ConfigureAwait(false);
            var selectorHints = EditorAssetCatalog.GetSelectorHints(page.Slug);

            var anyChanges = false;
            foreach (var section in page.Sections)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var selector = ResolveSelector(document, page.Slug, section, selectorHints);
                if (selector is null)
                {
                    continue;
                }

                var sectionChanged = false;

                if (!string.Equals(section.CssSelector, selector, StringComparison.Ordinal))
                {
                    section.CssSelector = selector;
                    sectionChanged = true;
                }

                var nodes = document.QuerySelectorAll(selector);
                if (nodes.Length == 0)
                {
                    continue;
                }

                switch (section.ContentType)
                {
                    case SectionContentType.Image:
                    {
                        var image = nodes.OfType<IHtmlImageElement>().FirstOrDefault() ??
                            nodes.Select(n => n.QuerySelector("img")).OfType<IHtmlImageElement>().FirstOrDefault();
                        if (image is null)
                        {
                            break;
                        }

                        var src = NormalizeMediaPath(image.GetAttribute("src"), image.Source);
                        var alt = image.AlternativeText;

                        if (!string.Equals(section.MediaPath, src, StringComparison.Ordinal))
                        {
                            section.MediaPath = src;
                            sectionChanged = true;
                        }

                        if (!string.Equals(section.MediaAltText, alt, StringComparison.Ordinal))
                        {
                            section.MediaAltText = alt;
                            sectionChanged = true;
                        }

                        break;
                    }

                    case SectionContentType.RichText:
                    case SectionContentType.Html:
                    {
                        var markup = nodes[0].InnerHtml.Trim();
                        if (!string.Equals(section.ContentValue, markup, StringComparison.Ordinal))
                        {
                            section.ContentValue = markup;
                            sectionChanged = true;
                        }
                        break;
                    }

                    default:
                    {
                        var element = nodes[0];
                        var markup = element.InnerHtml.Trim();
                        var hasMarkup = element.ChildElementCount > 0 || markup.Contains('<', StringComparison.Ordinal);

                        if (hasMarkup)
                        {
                            if (section.ContentType != SectionContentType.RichText)
                            {
                                section.ContentType = SectionContentType.RichText;
                                sectionChanged = true;
                            }

                            if (!string.Equals(section.ContentValue, markup, StringComparison.Ordinal))
                            {
                                section.ContentValue = markup;
                                sectionChanged = true;
                            }
                        }
                        else
                        {
                            var text = element.TextContent.Trim();
                            if (!string.Equals(section.ContentValue, text, StringComparison.Ordinal))
                            {
                                section.ContentValue = text;
                                sectionChanged = true;
                            }
                        }

                        break;
                    }
                }

                if (sectionChanged)
                {
                    var baseline = section.LastPublishedAtUtc ?? DateTime.UtcNow;
                    section.LastPublishedAtUtc = baseline;
                    section.UpdatedAtUtc = baseline;
                    anyChanges = true;
                }
            }

            if (anyChanges)
            {
                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Synchronized page {Slug} from static asset.", page.Slug);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("SyncPageFromAssetAsync cancelled for page {PageId}.", pageId);
        }
    }

    public async Task<IReadOnlyList<ContentChangeLog>> GetRecentLogsAsync(int take = 25, CancellationToken cancellationToken = default) =>
        await _db.ContentChangeLogs
            .AsNoTracking()
            .OrderByDescending(l => l.PerformedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<PublishResult> PublishPageAsync(Guid pageId, string userId, string userDisplayName, CancellationToken cancellationToken = default)
    {
        var page = await _db.SitePages
            .Include(p => p.Sections)
            .FirstOrDefaultAsync(p => p.Id == pageId, cancellationToken);

        if (page is null)
        {
            throw new InvalidOperationException("Page not found.");
        }

        var warnings = await UpdateLegacyAssetAsync(page, cancellationToken);

        var publishTimestamp = DateTime.UtcNow;
        foreach (var section in page.Sections)
        {
            section.LastPublishedAtUtc = publishTimestamp;
        }

        page.UpdatedAtUtc = publishTimestamp;

        _db.ContentChangeLogs.Add(new ContentChangeLog
        {
            SitePageId = page.Id,
            FieldKey = $"{page.Slug}::publish",
            ChangeSummary = "Page published to live experience",
            PerformedByUserId = userId,
            PerformedByDisplayName = userDisplayName,
            PerformedAtUtc = publishTimestamp
        });

        await _db.SaveChangesAsync(cancellationToken);
        return new PublishResult(publishTimestamp, warnings);
    }

    private async Task<IReadOnlyList<string>> UpdateLegacyAssetAsync(SitePage page, CancellationToken cancellationToken)
    {
        if (!EditorAssetCatalog.TryResolveAsset(page.Slug, out var assetFile))
        {
            _logger.LogWarning("Publish for {Slug} skipped: no asset mapping.", page.Slug);
            return Array.Empty<string>();
        }

        var legacySiteRoot = Path.GetFullPath(Path.Combine(_environment.ContentRootPath ?? string.Empty, ".."));
        var assetPath = Path.Combine(legacySiteRoot, assetFile);
        if (!File.Exists(assetPath))
        {
            throw new InvalidOperationException($"Static asset '{assetPath}' is missing.");
        }

        var html = await File.ReadAllTextAsync(assetPath, cancellationToken);
        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(html, cancellationToken);

        var selectorHints = EditorAssetCatalog.GetSelectorHints(page.Slug);
        var missingSelectors = new List<string>();

        foreach (var section in page.Sections.OrderBy(s => s.DisplayOrder))
        {
            var selector = ResolveSelector(document, page.Slug, section, selectorHints);
            if (selector is null)
            {
                missingSelectors.Add(section.SectionKey);
                continue;
            }

            if (!string.Equals(section.CssSelector, selector, StringComparison.Ordinal))
            {
                section.CssSelector = selector;
            }

            var elements = document.QuerySelectorAll(selector);
            if (elements.Length == 0)
            {
                missingSelectors.Add(section.SectionKey);
                continue;
            }

            foreach (var element in elements)
            {
                element.SetAttribute("data-bugence-section", section.SectionKey);
                element.SetAttribute("data-bugence-selector", selector);
                ApplySectionToElement(section, element);
            }
        }

        if (document.DocumentElement is null)
        {
            throw new InvalidOperationException("Unable to render updated HTML document.");
        }

        var outputBuilder = new StringBuilder();
        if (document.Doctype is { } doctype)
        {
            outputBuilder.Append("<!DOCTYPE ");
            outputBuilder.Append(string.IsNullOrWhiteSpace(doctype.Name) ? "html" : doctype.Name);

            if (!string.IsNullOrWhiteSpace(doctype.PublicIdentifier))
            {
                outputBuilder.Append(" PUBLIC \"");
                outputBuilder.Append(doctype.PublicIdentifier);
                outputBuilder.Append('"');
                if (!string.IsNullOrWhiteSpace(doctype.SystemIdentifier))
                {
                    outputBuilder.Append(" \"");
                    outputBuilder.Append(doctype.SystemIdentifier);
                    outputBuilder.Append('"');
                }
            }
            else if (!string.IsNullOrWhiteSpace(doctype.SystemIdentifier))
            {
                outputBuilder.Append(" SYSTEM \"");
                outputBuilder.Append(doctype.SystemIdentifier);
                outputBuilder.Append('"');
            }

            outputBuilder.Append('>');
            outputBuilder.AppendLine();
        }

        outputBuilder.Append(document.DocumentElement.OuterHtml);

        await File.WriteAllTextAsync(assetPath, outputBuilder.ToString(), cancellationToken);
        _logger.LogInformation("Published page {Slug} to {Path}", page.Slug, assetPath);
        if (missingSelectors.Count > 0)
        {
            _logger.LogWarning("Publish completed for {Slug} with unresolved selectors: {Selectors}", page.Slug, string.Join(", ", missingSelectors));
        }

        return missingSelectors;
    }

    private static string? ResolveSelector(IHtmlDocument document, string slug, PageSection section, IReadOnlyDictionary<string, string> selectorHints)
    {
        if (!string.IsNullOrWhiteSpace(section.CssSelector))
        {
            try
            {
                if (document.QuerySelector(section.CssSelector) is not null)
                {
                    return section.CssSelector;
                }
            }
            catch
            {
                // fall through to evaluate hints
            }
        }

        if (selectorHints.TryGetValue(section.SectionKey, out var hintValue) && !string.IsNullOrWhiteSpace(hintValue))
        {
            foreach (var candidate in hintValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    if (document.QuerySelector(candidate) is not null)
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // ignore invalid selector syntax
                }
            }
        }

        // As a final attempt, try to identify the element we previously tagged by data attribute.
        var fallback = document.QuerySelector($"[data-bugence-section=\"{section.SectionKey}\"]");
        if (fallback is not null)
        {
            return fallback.GetAttribute("data-bugence-selector") ?? BuildStableSelector(fallback);
        }

        return null;
    }

    private static void ApplySectionToElement(PageSection section, IElement element)
    {
        switch (section.ContentType)
        {
            case SectionContentType.Image:
                UpdateImageElement(section, element);
                break;

            case SectionContentType.Html:
            case SectionContentType.RichText:
                element.InnerHtml = section.ContentValue ?? string.Empty;
                break;

            default:
                element.TextContent = section.ContentValue ?? string.Empty;
                break;
        }
    }

    private static string? NormalizeMediaPath(string? attributeValue, string? computedValue)
    {
        var candidate = string.IsNullOrWhiteSpace(attributeValue) ? computedValue : attributeValue;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var trimmed = candidate.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            if (string.Equals(absolute.Scheme, "about", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrEmpty(absolute.AbsolutePath) ? null : absolute.AbsolutePath;
            }

            return trimmed;
        }

        if (trimmed.StartsWith("./", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            trimmed = "/" + trimmed.TrimStart('/');
        }

        return trimmed;
    }

    private static void UpdateImageElement(PageSection section, IElement element)
    {
        var imageElement = element as IHtmlImageElement ?? element.QuerySelector("img") as IHtmlImageElement;
        if (imageElement is null)
        {
            throw new InvalidOperationException($"Selector '{section.CssSelector}' does not point to an image element.");
        }

        if (!string.IsNullOrWhiteSpace(section.MediaPath))
        {
            imageElement.Source = section.MediaPath;
        }

        if (!string.IsNullOrWhiteSpace(section.MediaAltText))
        {
            imageElement.AlternativeText = section.MediaAltText;
        }
    }

    private static string BuildStableSelector(IElement element)
    {
        var segments = new List<string>();
        var current = element;
        while (current is not null && current.NodeType == NodeType.Element && current is not IHtmlDocument)
        {
            var segment = current.NodeName.ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(current.Id))
            {
                segment = $"#{current.Id}";
                segments.Insert(0, segment);
                break;
            }

            if (current.ClassList.Length > 0)
            {
                segment += $".{string.Join(".", current.ClassList.Select(CssEscape))}";
            }

            var parent = current.ParentElement;
            if (parent is not null)
            {
                var siblings = parent.Children.Where(c => c.NodeName == current.NodeName).ToList();
                if (siblings.Count > 1)
                {
                    var position = siblings.IndexOf(current) + 1;
                    segment += $":nth-of-type({position})";
                }
            }

            segments.Insert(0, segment);
            current = current.ParentElement;
        }

        return segments.Count > 0 ? string.Join(" > ", segments) : element.NodeName.ToLowerInvariant();
    }

    private static string CssEscape(string value) => string.Join(string.Empty, value.Select(EscapeChar));

    private static string EscapeChar(char c)
    {
        return char.IsLetterOrDigit(c) ? c.ToString() : $"\\{(int)c:x} ";
    }

    private static string? Truncate(string? value, int maxLen = 500) =>
        string.IsNullOrWhiteSpace(value) ? value : value.Length <= maxLen ? value : $"{value[..maxLen]}…";

    private static string BuildChangeSummary(PageSection section, string? newContent, string? oldContent, string? oldMediaPath, string? newMediaPath)
    {
        var messages = new List<string>();

        if (section.ContentType is SectionContentType.Text or SectionContentType.Html or SectionContentType.RichText)
        {
            if (!string.Equals(newContent, oldContent, StringComparison.Ordinal))
            {
                messages.Add("Text content updated");
            }
        }

        if (!string.Equals(oldMediaPath, newMediaPath, StringComparison.OrdinalIgnoreCase))
        {
            messages.Add("Media asset replaced");
        }

        if (!messages.Any())
        {
            messages.Add("Metadata updated");
        }

        return string.Join(" • ", messages);
    }
}




