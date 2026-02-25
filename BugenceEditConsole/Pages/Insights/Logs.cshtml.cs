
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AppCenter.Analytics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Insights;

public class LogsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public LogsModel(ApplicationDbContext db)
    {
        _db = db;
    }

    private const int DefaultPageSize = 12;

    public IReadOnlyList<ContentChangeLog> Logs { get; private set; } = Array.Empty<ContentChangeLog>();
    public IReadOnlyList<ContentChangeLog> LatestLogs { get; private set; } = Array.Empty<ContentChangeLog>();

    [BindProperty(SupportsGet = true)]
    public DateTime? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Term { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;

    public int TotalChanges { get; private set; }

    public int UniqueEditors { get; private set; }

    public int TotalFilteredChanges { get; private set; }

    public int TotalPages { get; private set; }

    public int PageSize => DefaultPageSize;

    public int ShowingFrom { get; private set; }

    public int ShowingTo { get; private set; }

    public InsightsAnalyticsSummary Analytics { get; private set; } = InsightsAnalyticsSummary.Empty;


    public async Task OnGetAsync()
    {
        var filteredQuery = _db.ContentChangeLogs.AsNoTracking().AsQueryable();

        DateTime? fromUtc = null;
        if (From.HasValue)
        {
            fromUtc = DateTime.SpecifyKind(From.Value.Date, DateTimeKind.Utc);
            filteredQuery = filteredQuery.Where(l => l.PerformedAtUtc >= fromUtc.Value);
        }

        DateTime? toUtcExclusive = null;
        if (To.HasValue)
        {
            toUtcExclusive = DateTime.SpecifyKind(To.Value.Date.AddDays(1), DateTimeKind.Utc);
            filteredQuery = filteredQuery.Where(l => l.PerformedAtUtc < toUtcExclusive.Value);
        }

        if (!string.IsNullOrWhiteSpace(Term))
        {
            var lowered = Term.Trim().ToLowerInvariant();
            filteredQuery = filteredQuery.Where(l =>
                (l.FieldKey != null && l.FieldKey.ToLower().Contains(lowered)) ||
                (l.ChangeSummary != null && l.ChangeSummary.ToLower().Contains(lowered)) ||
                (l.PerformedByDisplayName != null && l.PerformedByDisplayName.ToLower().Contains(lowered)));
        }

        TotalFilteredChanges = await filteredQuery.CountAsync();

        TotalPages = TotalFilteredChanges == 0
            ? 1
            : (int)Math.Ceiling(TotalFilteredChanges / (double)PageSize);

        PageIndex = Math.Clamp(PageIndex <= 0 ? 1 : PageIndex, 1, TotalPages);

        var skip = (PageIndex - 1) * PageSize;
        var logsQuery = filteredQuery
            .OrderByDescending(l => l.PerformedAtUtc);

        Logs = await logsQuery
            .Skip(skip)
            .Take(PageSize)
            .ToListAsync();

        // Latest 8 changes for the feed
        LatestLogs = await _db.ContentChangeLogs
            .AsNoTracking()
            .OrderByDescending(l => l.PerformedAtUtc)
            .Take(8)
            .ToListAsync();

        if (TotalFilteredChanges == 0)
        {
            ShowingFrom = 0;
            ShowingTo = 0;
        }
        else
        {
            ShowingFrom = skip + 1;
            ShowingTo = skip + Logs.Count;
        }

        TotalChanges = await _db.ContentChangeLogs.CountAsync();
        UniqueEditors = await _db.ContentChangeLogs.Select(l => l.PerformedByUserId).Distinct().CountAsync();

        var trendRaw = await filteredQuery
            .GroupBy(l => l.PerformedAtUtc.Date)
            .Select(g => new
            {
                Day = g.Key,
                Changes = g.Count(),
                UniqueEditors = g.Select(x => x.PerformedByUserId).Distinct().Count()
            })
            .OrderBy(result => result.Day)
            .ToListAsync();

        var trendPoints = trendRaw
            .Select(result => new InsightsAnalyticsSummary.ChangeTrendPoint(
                DateTime.SpecifyKind(result.Day, DateTimeKind.Utc),
                result.Changes,
                result.UniqueEditors))
            .ToList();

        var topEditorsRaw = await filteredQuery
            .Select(l => new
            {
                Label = (l.PerformedByDisplayName ?? l.PerformedByUserId) ?? "Unknown editor"
            })
            .GroupBy(l => l.Label)
            .Select(g => new { g.Key, Changes = g.Count() })
            .OrderByDescending(entry => entry.Changes)
            .ThenBy(entry => entry.Key)
            .Take(5)
            .ToListAsync();

        var topFieldsRaw = await filteredQuery
            .Select(l => new
            {
                Label = string.IsNullOrWhiteSpace(l.FieldKey) ? "Unspecified field" : l.FieldKey!
            })
            .GroupBy(l => l.Label)
            .Select(g => new { g.Key, Changes = g.Count() })
            .OrderByDescending(entry => entry.Changes)
            .ThenBy(entry => entry.Key)
            .Take(5)
            .ToListAsync();

        var fallbackEnd = toUtcExclusive?.AddDays(-1).Date ?? DateTime.UtcNow.Date;
        var fallbackStart = fromUtc?.Date ?? fallbackEnd.AddDays(-13);

        var rangeStart = trendPoints.Count > 0
            ? DateTime.SpecifyKind(trendPoints.Min(p => p.DayUtc.Date), DateTimeKind.Utc)
            : DateTime.SpecifyKind(fallbackStart, DateTimeKind.Utc);

        var rangeEnd = trendPoints.Count > 0
            ? DateTime.SpecifyKind(trendPoints.Max(p => p.DayUtc.Date), DateTimeKind.Utc)
            : DateTime.SpecifyKind(fallbackEnd, DateTimeKind.Utc);

        if (rangeStart > rangeEnd)
        {
            rangeStart = rangeEnd;
        }

        if (!From.HasValue && !To.HasValue && (rangeEnd - rangeStart).Days < 13)
        {
            rangeStart = rangeEnd.AddDays(-13);
        }

        var expandedTrend = ExpandTrend(rangeStart, rangeEnd, trendPoints);
        var topEditors = topEditorsRaw
            .Select(entry => new InsightsAnalyticsSummary.TopChangeEntry(entry.Key, entry.Changes))
            .ToList();
        var topFields = topFieldsRaw
            .Select(entry => new InsightsAnalyticsSummary.TopChangeEntry(entry.Key, entry.Changes))
            .ToList();

        Analytics = new InsightsAnalyticsSummary
        {
            RangeStartUtc = rangeStart,
            RangeEndUtc = rangeEnd,
            TotalChanges = expandedTrend.Sum(point => point.Changes),
            Trend = expandedTrend,
            TopEditors = topEditors,
            TopFields = topFields,
            Storyline = BuildStoryline(expandedTrend, topEditors, topFields, rangeStart, rangeEnd)
        };

        // Demo fallback when there is no data at all
        if (Analytics.TotalChanges == 0)
        {
            var today = DateTime.UtcNow.Date;
            var demoTrend = Enumerable.Range(0, 14)
                .Select(i => new InsightsAnalyticsSummary.ChangeTrendPoint(today.AddDays(-i), Random.Shared.Next(1, 6), Random.Shared.Next(1, 3)))
                .Reverse()
                .ToList();

            var demoEditors = new List<InsightsAnalyticsSummary.TopChangeEntry>
            {
                new("Hadi", 14),
                new("Pete D", 10),
                new("Jac", 8)
            };

            var demoFields = new List<InsightsAnalyticsSummary.TopChangeEntry>
            {
                new("Hero Section Headline", 6),
                new("Join Community Button", 5),
                new("Footer CTA", 3)
            };

            Analytics = new InsightsAnalyticsSummary
            {
                RangeStartUtc = today.AddDays(-13),
                RangeEndUtc = today,
                TotalChanges = demoTrend.Sum(x => x.Changes),
                Trend = demoTrend,
                TopEditors = demoEditors,
                TopFields = demoFields,
                Storyline = new List<InsightsAnalyticsSummary.StorylineEvent>
                {
                    new("Peak editing day", today.AddDays(-2), "Captured 12 changes across 3 editors.", "fa-solid fa-chart-line", 12, null),
                    new("Change velocity", today, "Editing momentum is accelerating (+2.1 changes/day)", "fa-solid fa-rocket", 2, "accelerating"),
                    new("Most active editor", today, "Hadi shipped 14 changes this week.", "fa-solid fa-user-astronaut", 14, "Hadi"),
                    new("Hotspot field", today, "Field 'Hero Section Headline' saw 6 edits.", "fa-solid fa-bullseye", 6, "Hero Section Headline")
                }
            };
        }
    }

    private static IReadOnlyList<InsightsAnalyticsSummary.ChangeTrendPoint> ExpandTrend(
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        IReadOnlyList<InsightsAnalyticsSummary.ChangeTrendPoint> points)
    {
        var lookup = points.ToDictionary(point => point.DayUtc.Date);
        var result = new List<InsightsAnalyticsSummary.ChangeTrendPoint>();

        for (var cursor = rangeStartUtc.Date; cursor <= rangeEndUtc.Date; cursor = cursor.AddDays(1))
        {
            var dayUtc = DateTime.SpecifyKind(cursor, DateTimeKind.Utc);
            if (lookup.TryGetValue(cursor, out var point))
            {
                result.Add(point with { DayUtc = dayUtc });
            }
            else
            {
                result.Add(new InsightsAnalyticsSummary.ChangeTrendPoint(dayUtc, 0, 0));
            }
        }

        return result;
    }

    private static IReadOnlyList<InsightsAnalyticsSummary.StorylineEvent> BuildStoryline(
        IReadOnlyList<InsightsAnalyticsSummary.ChangeTrendPoint> trend,
        IReadOnlyList<InsightsAnalyticsSummary.TopChangeEntry> topEditors,
        IReadOnlyList<InsightsAnalyticsSummary.TopChangeEntry> topFields,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc)
    {
        var storyline = new List<InsightsAnalyticsSummary.StorylineEvent>();

        var peakDay = trend
            .OrderByDescending(point => point.Changes)
            .ThenByDescending(point => point.UniqueEditors)
            .FirstOrDefault(point => point.Changes > 0);

        if (peakDay != null)
        {
            storyline.Add(new InsightsAnalyticsSummary.StorylineEvent(
                "Peak editing day",
                peakDay.DayUtc,
                $"Captured {peakDay.Changes} changes across {peakDay.UniqueEditors} editor(s).",
                "fa-solid fa-chart-line",
                peakDay.Changes,
                null));
        }

        if (trend.Count >= 4)
        {
            var midpoint = trend.Count / 2;
            var firstHalf = trend.Take(midpoint).Average(point => point.Changes);
            var secondHalf = trend.Skip(midpoint).Average(point => point.Changes);
            var delta = secondHalf - firstHalf;

            if (Math.Abs(delta) >= 1)
            {
                var direction = delta > 0 ? "accelerating" : "cooling";
                var icon = delta > 0 ? "fa-solid fa-rocket" : "fa-solid fa-gauge";
                storyline.Add(new InsightsAnalyticsSummary.StorylineEvent(
                    "Change velocity",
                    rangeEndUtc,
                    $"Editing momentum is {direction} ({Math.Abs(delta):0.#} more changes per day vs. earlier in the range).",
                    icon,
                    (int)Math.Round(delta),
                    direction));
            }
        }

        var leadEditor = topEditors.FirstOrDefault();
        if (leadEditor != null)
        {
            storyline.Add(new InsightsAnalyticsSummary.StorylineEvent(
                "Most active editor",
                rangeEndUtc,
                $"{leadEditor.Label} shipped {leadEditor.Changes} change(s) in this range.",
                "fa-solid fa-user-astronaut",
                leadEditor.Changes,
                leadEditor.Label));
        }

        var hotspotField = topFields.FirstOrDefault();
        if (hotspotField != null)
        {
            storyline.Add(new InsightsAnalyticsSummary.StorylineEvent(
                "Hotspot field",
                rangeEndUtc,
                $"Field '{hotspotField.Label}' saw {hotspotField.Changes} change(s).",
                "fa-solid fa-bullseye",
                hotspotField.Changes,
                hotspotField.Label));
        }

        if (!storyline.Any())
        {
            storyline.Add(new InsightsAnalyticsSummary.StorylineEvent(
                "Quiet range",
                rangeEndUtc,
                "No activity detected for the selected filters. Time to schedule a content refresh?",
                "fa-solid fa-moon",
                null,
                null));
        }

        return storyline;
    }

    public sealed class InsightsAnalyticsSummary
    {
        public DateTime RangeStartUtc { get; init; } = DateTime.UtcNow;

        public DateTime RangeEndUtc { get; init; } = DateTime.UtcNow;

        public int TotalChanges { get; init; }

        public IReadOnlyList<ChangeTrendPoint> Trend { get; init; } = Array.Empty<ChangeTrendPoint>();

        public IReadOnlyList<TopChangeEntry> TopEditors { get; init; } = Array.Empty<TopChangeEntry>();

        public IReadOnlyList<TopChangeEntry> TopFields { get; init; } = Array.Empty<TopChangeEntry>();

        public IReadOnlyList<StorylineEvent> Storyline { get; init; } = Array.Empty<StorylineEvent>();

        public static InsightsAnalyticsSummary Empty { get; } = new();

        public sealed record ChangeTrendPoint(DateTime DayUtc, int Changes, int UniqueEditors);

        public sealed record TopChangeEntry(string Label, int Changes);

        public sealed record StorylineEvent(string Title, DateTime HighlightUtc, string Summary, string Icon, int? Changes, string? Accent);
    }
}

