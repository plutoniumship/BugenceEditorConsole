using Microsoft.Data.Sqlite;

var dbPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "BugenceEditConsole", "app.db"));
Console.WriteLine($"Reading sections from {dbPath}\n");

using var connection = new SqliteConnection($"Data Source={dbPath}");
connection.Open();

var command = connection.CreateCommand();
command.CommandText = @"SELECT sp.Slug, ps.SectionKey, ps.Title, ps.ContentType, ps.IsLocked, ps.DisplayOrder, ps.CssSelector
FROM PageSections ps
JOIN SitePages sp ON sp.Id = ps.SitePageId
ORDER BY sp.Slug, ps.DisplayOrder";

using var reader = command.ExecuteReader();
string? currentSlug = null;
while (reader.Read())
{
    var slug = reader.GetString(0);
    if (!string.Equals(slug, currentSlug, StringComparison.OrdinalIgnoreCase))
    {
        currentSlug = slug;
        Console.WriteLine($"\n[{slug}]");
    }

    var sectionKey = reader.GetString(1);
    var title = reader.IsDBNull(2) ? "" : reader.GetString(2);
    var contentType = reader.GetString(3);
    var isLocked = reader.GetBoolean(4);
    var displayOrder = reader.GetInt32(5);
    var cssSelector = reader.IsDBNull(6) ? "" : reader.GetString(6);

    Console.WriteLine($"  - {displayOrder:D2} {sectionKey} ({contentType}{(isLocked ? ", locked" : string.Empty)})");
    if (!string.IsNullOrWhiteSpace(title))
    {
        Console.WriteLine($"      Title: {title}");
    }
    if (!string.IsNullOrWhiteSpace(cssSelector))
    {
        Console.WriteLine($"      Selector: {cssSelector}");
    }
}
