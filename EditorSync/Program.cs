using System.Linq;
using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Microsoft.Data.Sqlite;
using SQLitePCL;

var rootDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
Console.WriteLine($"Workspace root: {rootDir}\n");

var pages = new[]
{
    new PageConfig("index", Path.Combine(rootDir, "index.html"), "index"),
    new PageConfig("book-pete-d", Path.Combine(rootDir, "BookPeteD.html"), "book"),
    new PageConfig("meet-pete-d", Path.Combine(rootDir, "MeetPeteD.html"), "meet"),
    new PageConfig("join-community", Path.Combine(rootDir, "JoinTheCommunity.html"), "join"),
};
var pageConfigBySlug = pages.ToDictionary(p => p.Slug, StringComparer.OrdinalIgnoreCase);

var selectorList = "h1, h2, h3, h4, h5, h6, p, blockquote, li, a, button, span, figcaption, td, th, small, label, strong, em";
var config = Configuration.Default;
var context = BrowsingContext.New(config);

var pageSections = new List<PageSectionSnapshot>();

foreach (var page in pages)
{
    Console.WriteLine($"Processing {page.Slug} ({page.HtmlPath})");
    var html = await File.ReadAllTextAsync(page.HtmlPath);
    var document = await context.OpenAsync(req => req.Content(html));

    var body = document.Body ?? throw new InvalidOperationException("Document missing <body>");

    var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var existing in body.QuerySelectorAll("[data-bugence-section]"))
    {
        existingKeys.Add(existing.GetAttribute("data-bugence-section") ?? string.Empty);
    }

    var counter = 1;
    foreach (var element in body.QuerySelectorAll(selectorList))
    {
        if (element is null)
        {
            continue;
        }

        if (element.HasAttribute("data-bugence-section"))
        {
            continue;
        }

        if (!ShouldAnnotate(element))
        {
            continue;
        }

        var key = GenerateKey(page.Prefix, counter);
        while (existingKeys.Contains(key))
        {
            counter++;
            key = GenerateKey(page.Prefix, counter);
        }

        element.SetAttribute("data-bugence-section", key);
        existingKeys.Add(key);
        counter++;
    }

    var orderedElements = body.QuerySelectorAll("[data-bugence-section]")
        .OfType<IElement>()
        .Where(ShouldAnnotateFinal)
        .ToList();

    var builder = new StringBuilder();
    if (document.Doctype is not null)
    {
        builder.Append("<!DOCTYPE ");
        builder.Append(string.IsNullOrWhiteSpace(document.Doctype.Name) ? "html" : document.Doctype.Name);
        builder.Append('>');
        builder.AppendLine();
    }
    builder.Append(document.DocumentElement?.OuterHtml ?? string.Empty);
    await File.WriteAllTextAsync(page.HtmlPath, builder.ToString(), new UTF8Encoding(false));

    var order = 0;
    foreach (var element in orderedElements)
    {
        var key = element.GetAttribute("data-bugence-section") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            continue;
        }

        var type = DetermineContentType(element);
        var selector = BuildStableSelector(element);
        var title = BuildTitle(element);

        string? contentValue = null;
        string? mediaPath = null;
        string? mediaAlt = null;

        if (type == SectionContentType.Image)
        {
            mediaPath = element.GetAttribute("src");
            mediaAlt = element.GetAttribute("alt");
        }
        else
        {
            contentValue = element.InnerHtml.Trim();
        }

        pageSections.Add(new PageSectionSnapshot(page.Slug, key, title, type, selector, ++order, contentValue, mediaPath, mediaAlt));
    }
}

Console.WriteLine("\nUpdating database...");
Batteries_V2.Init();
var dbPath = Path.Combine(rootDir, "BugenceEditConsole", "app.db");
using var connectionDb = new SqliteConnection($"Data Source={dbPath}");
connectionDb.Open();

using var tx = connectionDb.BeginTransaction();

var pageIdCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
using (var cmd = connectionDb.CreateCommand())
{
    cmd.Transaction = tx;
    cmd.CommandText = "SELECT Id, Slug FROM SitePages";
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        var id = reader.GetGuid(0);
        var slug = reader.GetString(1);
        pageIdCache[slug] = id;
    }
}

var groupedByPage = pageSections.GroupBy(p => p.Slug);
foreach (var group in groupedByPage)
{
    if (!pageIdCache.TryGetValue(group.Key, out var pageId))
    {
        Console.WriteLine($"Skipping {group.Key}: page not found in DB.");
        continue;
    }

    var existingSections = new Dictionary<string, (Guid Id, string SectionKey)>(StringComparer.OrdinalIgnoreCase);
    using (var cmd = connectionDb.CreateCommand())
    {
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Id, SectionKey FROM PageSections WHERE SitePageId = @page";
        cmd.Parameters.AddWithValue("@page", pageId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetGuid(0);
            var key = reader.GetString(1);
            existingSections[key] = (id, key);
        }
    }

    var keysToKeep = new HashSet<string>(group.Select(g => g.SectionKey), StringComparer.OrdinalIgnoreCase);

    // Upsert sections
    foreach (var snapshot in group)
    {
        if (existingSections.TryGetValue(snapshot.SectionKey, out var existing))
        {
            using var update = connectionDb.CreateCommand();
            update.Transaction = tx;
            update.CommandText = @"UPDATE PageSections
SET Title = @title,
    ContentType = @type,
    ContentValue = @content,
    CssSelector = @selector,
    MediaPath = @mediaPath,
    MediaAltText = @mediaAlt,
    DisplayOrder = @order,
    UpdatedAtUtc = @updated
WHERE Id = @id";
            update.Parameters.AddWithValue("@title", snapshot.Title ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("@type", (int)snapshot.ContentType);
            update.Parameters.AddWithValue("@content", snapshot.ContentValue ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("@selector", snapshot.CssSelector ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("@mediaPath", snapshot.MediaPath ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("@mediaAlt", snapshot.MediaAlt ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("@order", snapshot.DisplayOrder);
            update.Parameters.AddWithValue("@updated", DateTime.UtcNow);
            update.Parameters.AddWithValue("@id", existing.Id);
            update.ExecuteNonQuery();
        }
        else
        {
            using var insert = connectionDb.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = @"INSERT INTO PageSections
(Id, SitePageId, SectionKey, Title, ContentType, ContentValue, CssSelector, MediaPath, MediaAltText, DisplayOrder, IsLocked, UpdatedAtUtc)
VALUES
(@id, @site, @key, @title, @type, @content, @selector, @mediaPath, @mediaAlt, @order, 0, @updated)";
            var id = Guid.NewGuid();
            insert.Parameters.AddWithValue("@id", id);
            insert.Parameters.AddWithValue("@site", pageId);
            insert.Parameters.AddWithValue("@key", snapshot.SectionKey);
            insert.Parameters.AddWithValue("@title", snapshot.Title ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("@type", (int)snapshot.ContentType);
            insert.Parameters.AddWithValue("@content", snapshot.ContentValue ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("@selector", snapshot.CssSelector ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("@mediaPath", snapshot.MediaPath ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("@mediaAlt", snapshot.MediaAlt ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("@order", snapshot.DisplayOrder);
            insert.Parameters.AddWithValue("@updated", DateTime.UtcNow);
            insert.ExecuteNonQuery();
        }
    }

    // Delete sections that are no longer present (only those with matching prefix)
    var prefix = pageConfigBySlug[group.Key].SectionKeyPrefix;
    var obsolete = existingSections.Keys.Except(keysToKeep, StringComparer.OrdinalIgnoreCase)
        .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .ToList();

    foreach (var key in obsolete)
    {
        using var delete = connectionDb.CreateCommand();
        delete.Transaction = tx;
        delete.CommandText = "DELETE FROM PageSections WHERE SitePageId = @page AND SectionKey = @key";
        delete.Parameters.AddWithValue("@page", pageId);
        delete.Parameters.AddWithValue("@key", key);
        delete.ExecuteNonQuery();
    }
}

tx.Commit();
Console.WriteLine("Sync complete.");

static string GenerateKey(string prefix, int counter) => $"{prefix}_block_{counter:D3}";

static bool ShouldAnnotate(IElement element)
{
    var tag = element.TagName.ToUpperInvariant();
    if (tag is "SCRIPT" or "STYLE" or "SVG" or "PATH" or "NOSCRIPT" or "VIDEO" or "AUDIO")
    {
        return false;
    }

    var text = element.TextContent?.Trim();
    if (tag != "IMG" && string.IsNullOrWhiteSpace(text))
    {
        return false;
    }

    var parentHasSection = element.ParentElement?.Closest("[data-bugence-section]");
    if (parentHasSection != null)
    {
        return false;
    }

    return true;
}

static bool ShouldAnnotateFinal(IElement element)
{
    var key = element.GetAttribute("data-bugence-section");
    if (string.IsNullOrWhiteSpace(key))
    {
        return false;
    }

    var ancestor = element.ParentElement?.Closest("[data-bugence-section]");
    return ancestor is null;
}

static SectionContentType DetermineContentType(IElement element)
{
    return element.TagName.ToUpperInvariant() switch
    {
        "IMG" => SectionContentType.Image,
        _ => SectionContentType.RichText
    };
}

static string BuildTitle(IElement element)
{
    var text = (element.TextContent ?? string.Empty).Trim();
    if (text.Length > 60)
    {
        text = text.Substring(0, 57) + "...";
    }
    return text;
}

static string BuildStableSelector(IElement element)
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
            segment += $".{string.Join('.', current.ClassList.Select(CssEscape))}";
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
        current = parent;
    }

    return segments.Count > 0 ? string.Join(" > ", segments) : element.NodeName.ToLowerInvariant();
}

static string CssEscape(string value) => string.Join(string.Empty, value.Select(EscapeChar));

static string EscapeChar(char c) => char.IsLetterOrDigit(c) ? c.ToString() : $"\\{(int)c:x} ";

record PageConfig(string Slug, string HtmlPath, string Prefix)
{
    public string SectionKeyPrefix => $"{Prefix}_block_";
}

record PageSectionSnapshot(
    string Slug,
    string SectionKey,
    string? Title,
    SectionContentType ContentType,
    string? CssSelector,
    int DisplayOrder,
    string? ContentValue,
    string? MediaPath,
    string? MediaAlt);

enum SectionContentType
{
    Text = 0,
    Html = 1,
    Image = 2,
    RichText = 3
}
