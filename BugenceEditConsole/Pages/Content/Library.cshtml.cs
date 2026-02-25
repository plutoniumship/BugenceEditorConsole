
using BugenceEditConsole.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Content;

public class LibraryModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public LibraryModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<PageSummary> Pages { get; private set; } = Array.Empty<PageSummary>();
    public IReadOnlyList<PageStatusMetric> StatusMetrics { get; private set; } = Array.Empty<PageStatusMetric>();
    public IReadOnlyList<RecentChange> RecentChanges { get; private set; } = Array.Empty<RecentChange>();
    public LibraryPulse Pulse { get; private set; } = LibraryPulse.Empty;
    public PageHighlight? Spotlight { get; private set; }
    public IReadOnlyList<PageCluster> Clusters { get; private set; } = Array.Empty<PageCluster>();

    [BindProperty(SupportsGet = true)]
    public string? Term { get; set; }

    public int TotalTextSections { get; private set; }

    public int TotalImageSections { get; private set; }

    public async Task OnGetAsync()
    {
        var query = _db.SitePages
            .Include(p => p.Sections)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(Term))
        {
            var lowered = Term.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.Name.ToLower().Contains(lowered) ||
                p.Slug.ToLower().Contains(lowered) ||
                (p.Description != null && p.Description.ToLower().Contains(lowered)));
        }

        var pages = await query
            .OrderBy(p => p.Name)
            .Select(p => new PageSummary(
                p.Id,
                p.Name,
                p.Slug,
                p.Description ?? "No description yet",
                p.Sections.Count,
                p.Sections.Count(s => s.ContentType != Models.SectionContentType.Image),
                p.Sections.Count(s => s.ContentType == Models.SectionContentType.Image),
                p.UpdatedAtUtc,
                p.Sections.Max(s => (DateTime?)s.LastPublishedAtUtc)))
            .ToListAsync();

        Pages = pages;

        TotalTextSections = pages.Sum(p => p.TextSections);
        TotalImageSections = pages.Sum(p => p.ImageSections);

        var totalSections = pages.Sum(p => p.SectionCount);
        var nowUtc = DateTime.UtcNow;
        var activeCutoff = nowUtc.AddDays(-7);
        var freshCutoff = nowUtc.AddDays(-2);

        Pulse = pages.Count == 0
            ? LibraryPulse.Empty
            : new LibraryPulse(
                pages.Count,
                totalSections,
                pages.Count(p => p.UpdatedAtUtc >= activeCutoff),
                pages.Max(p => p.UpdatedAtUtc));

        var spotlight = pages
            .OrderByDescending(p => p.SectionCount)
            .ThenByDescending(p => p.UpdatedAtUtc)
            .FirstOrDefault();

        if (spotlight is not null)
        {
            Spotlight = new PageHighlight(
                spotlight.Id,
                spotlight.Name,
                spotlight.Description,
                "Content blocks",
                spotlight.SectionCount,
                spotlight.UpdatedAtUtc,
                spotlight.UpdatedAtUtc >= freshCutoff);
        }

        StatusMetrics = new[]
        {
            new PageStatusMetric("fa-solid fa-rocket", "Launch ready", pages.Count(p => p.SectionCount > 0)),
            new PageStatusMetric("fa-solid fa-spinner", "In review", pages.Count(p => p.SectionCount > 0 && p.UpdatedAtUtc > DateTime.UtcNow.AddDays(-2))),
            new PageStatusMetric("fa-solid fa-flask", "Experimenting", pages.Count(p => p.Description.Contains("test", StringComparison.OrdinalIgnoreCase)))
        };

        RecentChanges = pages
            .OrderByDescending(p => p.UpdatedAtUtc)
            .Take(5)
            .Select(p => new RecentChange(
                p.Name,
                p.Description,
                p.UpdatedAtUtc))
            .ToList();

        Clusters = new[]
            {
                new PageCluster(
                    "Fresh transmissions",
                    "Updated in the last 48 hours",
                    pages.Where(p => p.UpdatedAtUtc >= freshCutoff).Take(5).ToList()),
                new PageCluster(
                    "Steady orbit",
                    "Live content with room to finesse",
                    pages.Where(p => p.SectionCount > 0 && p.UpdatedAtUtc < freshCutoff).Take(5).ToList()),
                new PageCluster(
                    "Blueprint drafts",
                    "Pages waiting for their first content drop",
                    pages.Where(p => p.SectionCount == 0).Take(5).ToList())
            }
            .Where(cluster => cluster.Items.Count > 0)
            .ToList();
    }

    public record PageSummary(
        Guid Id,
        string Name,
        string Slug,
        string Description,
        int SectionCount,
        int TextSections,
        int ImageSections,
        DateTime UpdatedAtUtc,
        DateTime? LastPublishedAtUtc);

    public record PageStatusMetric(string Icon, string Label, int Value);
    public record RecentChange(string PageName, string Summary, DateTime OccurredAtUtc);

    public record LibraryPulse(int TotalPages, int TotalSections, int ActivePastWeek, DateTime? LastUpdatedUtc)
    {
        public static LibraryPulse Empty { get; } = new(0, 0, 0, null);
    }

    public record PageHighlight(
        Guid PageId,
        string Name,
        string Description,
        string MetricLabel,
        int MetricValue,
        DateTime UpdatedAtUtc,
        bool IsFresh);

    public record PageCluster(
        string Title,
        string Subtitle,
        IReadOnlyList<PageSummary> Items);
}
