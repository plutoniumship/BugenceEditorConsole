using System.Text.RegularExpressions;
using BugenceEditConsole.Data;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Infrastructure;

public static partial class SlugGenerator
{
    private static readonly Regex InvalidCharacters = SlugRegex();

    public static string Slugify(string? seed)
    {
        if (string.IsNullOrWhiteSpace(seed))
        {
            return "project";
        }

        var normalized = InvalidCharacters.Replace(seed.ToLowerInvariant(), "-")
            .Trim('-');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "project";
        }

        return normalized;
    }

    public static async Task<string> GenerateProjectSlugAsync(
        ApplicationDbContext db,
        string? preferredValue,
        CancellationToken cancellationToken = default)
    {
        var baseSlug = Slugify(preferredValue);
        var slug = baseSlug;
        var counter = 1;

        while (await db.UploadedProjects.AsNoTracking().AnyAsync(p => p.Slug == slug, cancellationToken))
        {
            counter++;
            slug = $"{baseSlug}-{counter}";
        }

        return slug;
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SlugRegex();
}
