using System.Security.Claims;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Pages.Analytics;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BugenceEditConsole.Tests;

public sealed class AnalyticsPageModelTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;

    public AnalyticsPageModelTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
    }

    [Fact]
    public async Task OnGetTabAsync_NormalizesAndClampsQuery_BeforeCallingAnalyticsService()
    {
        var user = await SeedEligibleProjectAsync();
        var analytics = new FakeAnalyticsQueryService();
        var model = BuildModel(user, analytics, new FakeGoogleSearchConsoleService());

        var result = await model.OnGetTabAsync(
            projectId: 101,
            range: "junk",
            tab: "not-real",
            compare: "true",
            segment: "unknown",
            module: "source_medium",
            dimension: "browser",
            funnelMode: "open",
            funnelId: null,
            country: "US",
            device: "desktop",
            landingPage: "/",
            referrer: "google.com",
            utmSource: "google",
            utmMedium: "organic",
            utmCampaign: "brand",
            page: 0,
            pageSize: 999,
            search: "contact",
            sortBy: "views",
            sortDir: "desc");

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(200, json.StatusCode ?? 200);
        Assert.NotNull(analytics.LastQuery);
        Assert.Equal("30d", analytics.LastRange);
        Assert.Equal("overview", analytics.LastQuery!.Tab);
        Assert.True(analytics.LastQuery.Compare);
        Assert.Equal("all", analytics.LastQuery.Segment);
        Assert.Equal(1, analytics.LastQuery.Page);
        Assert.Equal(100, analytics.LastQuery.PageSize);
        Assert.Equal("contact", analytics.LastQuery.Search);
        Assert.Equal("views", analytics.LastQuery.SortBy);
        Assert.Equal("desc", analytics.LastQuery.SortDir);
        Assert.Equal("browser", analytics.LastQuery.Dimension);
        Assert.Equal("source_medium", analytics.LastQuery.Module);
        Assert.Equal("US", analytics.LastQuery.Filters.Country);
        Assert.Equal("desktop", analytics.LastQuery.Filters.Device);
        Assert.Equal("/", analytics.LastQuery.Filters.LandingPage);
    }

    [Fact]
    public async Task OnGetTabAsync_ReturnsBadRequest_ForIneligibleProject()
    {
        var user = await SeedEligibleProjectAsync();
        var model = BuildModel(user, new FakeAnalyticsQueryService(), new FakeGoogleSearchConsoleService());

        var result = await model.OnGetTabAsync(
            projectId: 999,
            range: "24h",
            tab: "overview",
            compare: "0",
            segment: "all",
            module: null,
            dimension: null,
            funnelMode: null,
            funnelId: null,
            country: null,
            device: null,
            landingPage: null,
            referrer: null,
            utmSource: null,
            utmMedium: null,
            utmCampaign: null);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(400, json.StatusCode);
    }

    [Fact]
    public async Task OnGetSeoConnectorStatusAsync_ReturnsConnectorPayload_ForEligibleProject()
    {
        var user = await SeedEligibleProjectAsync();
        var searchConsole = new FakeGoogleSearchConsoleService
        {
            Status = new GoogleSearchConsoleStatus(
                Connected: true,
                LastSyncUtc: new DateTime(2026, 2, 27, 10, 0, 0, DateTimeKind.Utc),
                SelectedProperty: "sc-domain:example.com",
                AuthScopeState: "connected",
                HasCachedData: true)
        };
        var model = BuildModel(user, new FakeAnalyticsQueryService(), searchConsole);

        var result = await model.OnGetSeoConnectorStatusAsync(101);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(200, json.StatusCode ?? 200);
        Assert.Equal(101, searchConsole.LastProjectId);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<ApplicationUser> SeedEligibleProjectAsync()
    {
        var user = new ApplicationUser
        {
            Id = "owner-1",
            UserName = "owner@example.com",
            Email = "owner@example.com"
        };

        _db.Users.Add(user);
        _db.UploadedProjects.Add(new UploadedProject
        {
            Id = 101,
            FolderName = "Example Project",
            Slug = "example-project",
            DisplayName = "Example Project",
            UserId = user.Id,
            OriginalFileName = "example.zip",
            Data = Array.Empty<byte>()
        });
        _db.ProjectDomains.Add(new ProjectDomain
        {
            Id = Guid.NewGuid(),
            UploadedProjectId = 101,
            DomainName = "example.com",
            NormalizedDomain = "example.com",
            DomainType = ProjectDomainType.Custom,
            Status = DomainStatus.Connected,
            SslStatus = DomainSslStatus.Active,
            UpdatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        return user;
    }

    private IndexModel BuildModel(
        ApplicationUser? user,
        IAnalyticsQueryService analytics,
        IGoogleSearchConsoleService searchConsole)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                user is null ? Array.Empty<Claim>() : new[] { new Claim(ClaimTypes.NameIdentifier, user.Id) },
                "TestAuth"))
        };

        var pageContext = new PageContext
        {
            HttpContext = httpContext,
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        };

        return new IndexModel(
            _db,
            new FakeUserManager(user),
            analytics,
            searchConsole)
        {
            PageContext = pageContext
        };
    }

    private sealed class FakeAnalyticsQueryService : IAnalyticsQueryService
    {
        public AnalyticsTabQuery? LastQuery { get; private set; }
        public string? LastRange { get; private set; }

        public Task<AnalyticsSnapshot> GetProjectSnapshotAsync(string ownerUserId, Guid? companyId, int projectId, string? range, CancellationToken cancellationToken = default)
            => Task.FromResult(AnalyticsSnapshot.Empty(projectId, range ?? "30d"));

        public Task<AnalyticsSnapshot> GetProjectFunnelSnapshotAsync(string ownerUserId, Guid? companyId, int projectId, string? range, CancellationToken cancellationToken = default)
            => Task.FromResult(AnalyticsSnapshot.Empty(projectId, range ?? "30d"));

        public Task<AnalyticsSnapshot> GetSnapshotAsync(string ownerUserId, Guid? companyId, CancellationToken cancellationToken = default)
            => Task.FromResult(AnalyticsSnapshot.Empty(0, "30d"));

        public Task<AnalyticsTabPayload> GetTabPayloadAsync(string ownerUserId, Guid? companyId, int projectId, string? range, AnalyticsTabQuery query, CancellationToken cancellationToken = default)
        {
            LastRange = range;
            LastQuery = query;
            return Task.FromResult(new AnalyticsTabPayload(
                Tab: query.Tab,
                Metrics: new[] { new AnalyticsKeyMetric("views", "Views", 12, 6) },
                Series: new[] { new AnalyticsSeriesPoint("Now", 12, 6) },
                Lists: new[] { new AnalyticsListMetric("Top", 12) },
                Table: new AnalyticsTablePayload(
                    new[] { new AnalyticsTableColumn("dimension", "Dimension") },
                    new[] { new AnalyticsTableRow(new Dictionary<string, string> { ["dimension"] = "Example" }) },
                    1,
                    query.Page,
                    query.PageSize),
                Extras: new Dictionary<string, object?> { ["contractVersion"] = "v2" }));
        }
    }

    private sealed class FakeGoogleSearchConsoleService : IGoogleSearchConsoleService
    {
        public GoogleSearchConsoleStatus Status { get; set; } = new(false, null, null, "disconnected", false);
        public int? LastProjectId { get; private set; }

        public Task<GoogleSearchConsoleStatus> GetStatusAsync(string ownerUserId, Guid? companyId, int projectId, CancellationToken cancellationToken = default)
        {
            LastProjectId = projectId;
            return Task.FromResult(Status);
        }

        public Task<IReadOnlyList<string>> GetPropertiesAsync(string ownerUserId, Guid? companyId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task SetSelectedPropertyAsync(string ownerUserId, Guid? companyId, int projectId, string propertyUri, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> SyncAsync(string ownerUserId, Guid? companyId, int projectId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<object?> GetLatestSnapshotAsync(string ownerUserId, Guid? companyId, int projectId, string snapshotType, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(null);
    }

    private sealed class FakeUserManager : UserManager<ApplicationUser>
    {
        private readonly ApplicationUser? _user;

        public FakeUserManager(ApplicationUser? user)
            : base(new FakeUserStore(), null, null, null, null, null, null, null, null)
        {
            _user = user;
        }

        public override Task<ApplicationUser?> GetUserAsync(ClaimsPrincipal principal)
            => Task.FromResult(_user);
    }

    private sealed class FakeUserStore : IUserStore<ApplicationUser>
    {
        public void Dispose()
        {
        }

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.Id);

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.UserName);

        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(user.NormalizedUserName);

        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
            => Task.FromResult(IdentityResult.Success);

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
            => Task.FromResult<ApplicationUser?>(null);

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
            => Task.FromResult<ApplicationUser?>(null);
    }
}
