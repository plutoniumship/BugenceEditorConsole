using Microsoft.Data.Sqlite;

var dbPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "BugenceEditConsole", "app.db"));
if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"Database not found at {dbPath}");
    return 1;
}

await using var connection = new SqliteConnection($"Data Source={dbPath}");
await connection.OpenAsync();

var command = connection.CreateCommand();
command.CommandText = @"
SELECT ps.SectionKey,
       ps.ContentType,
       IFNULL(ps.ContentValue, '') AS ContentValue,
       IFNULL(ps.MediaPath, '') AS MediaPath,
       IFNULL(ps.CssSelector, '') AS CssSelector
FROM PageSections ps
JOIN SitePages sp ON ps.SitePageId = sp.Id
WHERE sp.Slug = 'book-pete-d'
ORDER BY ps.DisplayOrder;
";

await using var reader = await command.ExecuteReaderAsync();
Console.WriteLine("Section\tType\tMediaPath\tSelector\tPreview");
while (await reader.ReadAsync())
{
    var key = reader.GetString(0);
    var type = reader.GetInt32(1);
    var media = reader.GetString(3);
    var selector = reader.GetString(4);
    var preview = reader.GetString(2);
    if (preview.Length > 40)
    {
        preview = preview[..37] + "...";
    }
    Console.WriteLine($"{key}\t{type}\t{media}\t{selector}\t{preview}");
}

return 0;
