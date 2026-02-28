using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BugenceEditConsole.Tests;

public sealed class AnalyticsServiceTests : IDisposable
{
    private const string OwnerUserId = "owner-1";
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly AnalyticsService _service;

    public AnalyticsServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApplicationDbContext(options);
        _service = new AnalyticsService(_db, NullLogger<AnalyticsService>.Instance);
    }

    [Fact]
    public async Task AcquisitionTab_FiltersToReferralSegment_AndKeepsSourceMediumRows()
    {
        var now = DateTime.UtcNow;

        await TrackSessionAsync(
            sessionId: "s-ref",
            path: "/",
            city: "London",
            country: "GB",
            referrer: "partner.example.com",
            userAgent: ChromeOnWindows,
            occurredAtUtc: now.AddMinutes(-10),
            utmSource: null,
            utmMedium: null);

        await TrackEventAsync(
            sessionId: "s-ref",
            eventType: "form_submit",
            eventName: "contact_form_submit",
            path: "/contact",
            occurredAtUtc: now.AddMinutes(-9),
            referrer: "partner.example.com",
            utmSource: "partner",
            utmMedium: "referral");

        await TrackSessionAsync(
            sessionId: "s-direct",
            path: "/",
            city: "New York",
            country: "US",
            referrer: null,
            userAgent: ChromeOnWindows,
            occurredAtUtc: now.AddMinutes(-8),
            utmSource: null,
            utmMedium: null);

        var payload = await _service.GetTabPayloadAsync(
            OwnerUserId,
            companyId: null,
            projectId: 101,
            range: "24h",
            query: new AnalyticsTabQuery(
                Tab: "acquisition",
                Compare: false,
                Segment: "referral",
                Filters: new AnalyticsQueryFilters(null, null, null, null, null, null, null)));

        Assert.Equal("acquisition", payload.Tab);
        Assert.Contains(payload.Metrics, x => x.Label == "Referral" && x.Value >= 1);
        Assert.DoesNotContain(payload.Metrics, x => x.Label == "Direct" && x.Value > 0);
        Assert.NotNull(payload.Table);
        Assert.Single(payload.Table!.Rows);
        Assert.Equal("partner/referral", payload.Table.Rows[0].Cells["source_medium"]);
    }

    [Fact]
    public async Task AudienceTab_CityAndBrowserDimensions_ReturnRows()
    {
        var now = DateTime.UtcNow;

        await TrackSessionAsync("s-city-1", "/", "Lahore", "PK", null, ChromeOnWindows, now.AddMinutes(-20));
        await TrackSessionAsync("s-city-2", "/pricing", "Lahore", "PK", null, ChromeOnWindows, now.AddMinutes(-18));
        await TrackSessionAsync("s-city-3", "/contact", "Berlin", "DE", null, SafariOniPhone, now.AddMinutes(-16));

        var cityPayload = await _service.GetTabPayloadAsync(
            OwnerUserId,
            null,
            101,
            "24h",
            new AnalyticsTabQuery(
                Tab: "audience",
                Compare: false,
                Segment: "all",
                Filters: new AnalyticsQueryFilters(null, null, null, null, null, null, null),
                Dimension: "city"));

        Assert.Equal("audience", cityPayload.Tab);
        Assert.NotNull(cityPayload.Table);
        Assert.Contains(cityPayload.Table!.Rows, row => row.Cells["dimension"] == "Lahore");
        Assert.Contains(cityPayload.Table.Rows, row => row.Cells["dimension"] == "Berlin");

        var browserPayload = await _service.GetTabPayloadAsync(
            OwnerUserId,
            null,
            101,
            "24h",
            new AnalyticsTabQuery(
                Tab: "audience",
                Compare: false,
                Segment: "all",
                Filters: new AnalyticsQueryFilters(null, null, null, null, null, null, null),
                Dimension: "browser"));

        Assert.NotNull(browserPayload.Table);
        Assert.Contains(browserPayload.Table!.Rows, row => row.Cells["dimension"] == "Chrome");
        Assert.Contains(browserPayload.Table.Rows, row => row.Cells["dimension"] == "Safari");
    }

    [Fact]
    public async Task ConversionsTab_CompareMode_ReturnsPreviousValues()
    {
        var now = DateTime.UtcNow;

        await TrackSessionAsync("s-prev", "/", "Chicago", "US", null, ChromeOnWindows, now.AddHours(-30));
        await TrackEventAsync("s-prev", "form_submit", "contact_form_submit", "/contact", now.AddHours(-29), null, null, null);

        await TrackSessionAsync("s-current-1", "/", "Chicago", "US", null, ChromeOnWindows, now.AddMinutes(-40));
        await TrackEventAsync("s-current-1", "form_submit", "contact_form_submit", "/contact", now.AddMinutes(-35), null, null, null);

        await TrackSessionAsync("s-current-2", "/", "Chicago", "US", null, ChromeOnWindows, now.AddMinutes(-30));
        await TrackEventAsync("s-current-2", "form_submit", "contact_form_submit", "/contact", now.AddMinutes(-28), null, null, null);

        var payload = await _service.GetTabPayloadAsync(
            OwnerUserId,
            null,
            101,
            "24h",
            new AnalyticsTabQuery(
                Tab: "conversions",
                Compare: true,
                Segment: "all",
                Filters: new AnalyticsQueryFilters(null, null, null, null, null, null, null)));

        var keyEvents = payload.Metrics.Single(x => x.Key == "key_events");
        Assert.Equal(2, keyEvents.Value);
        Assert.Equal(1, keyEvents.PreviousValue);
        Assert.Contains(payload.Series, point => point.PreviousValue.HasValue);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task TrackSessionAsync(
        string sessionId,
        string path,
        string? city,
        string country,
        string? referrer,
        string userAgent,
        DateTime occurredAtUtc,
        string? utmSource = null,
        string? utmMedium = null)
    {
        await _service.TrackPageViewAsync(new AnalyticsIngestContext(
            ProjectId: 101,
            Host: "example.com",
            Path: path,
            SessionId: sessionId,
            CountryCode: country,
            DeviceType: null,
            City: city,
            Language: "en-US",
            ReferrerHost: referrer,
            PageTitle: "Page",
            LandingPath: path,
            EngagementTimeMs: 12000,
            UtmSource: utmSource,
            UtmMedium: utmMedium,
            UtmCampaign: null,
            UtmTerm: null,
            UtmContent: null,
            UserAgent: userAgent,
            IsBot: false,
            OccurredAtUtc: occurredAtUtc,
            OwnerUserId: OwnerUserId,
            CompanyId: null));
    }

    private async Task TrackEventAsync(
        string sessionId,
        string eventType,
        string eventName,
        string path,
        DateTime occurredAtUtc,
        string? referrer,
        string? utmSource,
        string? utmMedium)
    {
        await _service.TrackEventAsync(new AnalyticsEventIngestContext(
            ProjectId: 101,
            SessionId: sessionId,
            EventType: eventType,
            EventName: eventName,
            Path: path,
            PageTitle: "Page",
            CountryCode: "US",
            DeviceType: null,
            Language: "en-US",
            ReferrerHost: referrer,
            MetadataJson: null,
            UtmSource: utmSource,
            UtmMedium: utmMedium,
            UtmCampaign: null,
            UtmTerm: null,
            UtmContent: null,
            OccurredAtUtc: occurredAtUtc,
            OwnerUserId: OwnerUserId,
            CompanyId: null));
    }

    private const string ChromeOnWindows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";
    private const string SafariOniPhone = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";
}
