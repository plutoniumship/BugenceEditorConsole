using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;

namespace BugenceEditConsole.Services;

public sealed record GoogleSearchConsoleStatus(
    bool Connected,
    DateTime? LastSyncUtc,
    string? SelectedProperty,
    string AuthScopeState,
    bool HasCachedData);

public interface IGoogleSearchConsoleService
{
    Task<GoogleSearchConsoleStatus> GetStatusAsync(string ownerUserId, Guid? companyId, int projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetPropertiesAsync(string ownerUserId, Guid? companyId, CancellationToken cancellationToken = default);
    Task SetSelectedPropertyAsync(string ownerUserId, Guid? companyId, int projectId, string propertyUri, CancellationToken cancellationToken = default);
    Task<bool> SyncAsync(string ownerUserId, Guid? companyId, int projectId, CancellationToken cancellationToken = default);
    Task<object?> GetLatestSnapshotAsync(string ownerUserId, Guid? companyId, int projectId, string snapshotType, CancellationToken cancellationToken = default);
}

public sealed class GoogleSearchConsoleService : IGoogleSearchConsoleService
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GoogleSearchConsoleService> _logger;
    private readonly IConfiguration _configuration;

    public GoogleSearchConsoleService(
        ApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleSearchConsoleService> logger,
        IConfiguration configuration)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<GoogleSearchConsoleStatus> GetStatusAsync(string ownerUserId, Guid? companyId, int projectId, CancellationToken cancellationToken = default)
    {
        await EnsureSeoTablesAsync(cancellationToken);
        var query = _db.IntegrationConnections.AsNoTracking()
            .Where(x => x.Provider == "google_search_console" && x.OwnerUserId == ownerUserId);
        if (companyId.HasValue)
        {
            query = query.Where(x => x.CompanyId == companyId.Value);
        }
        var connection = await query.OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return new GoogleSearchConsoleStatus(false, null, null, "disconnected", false);
        }

        var latestSync = await _db.AnalyticsSeoSnapshots.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId && x.ProjectId == projectId && x.SnapshotType == "search_performance")
            .OrderByDescending(x => x.CapturedAtUtc)
            .Select(x => (DateTime?)x.CapturedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var hasCache = latestSync.HasValue;
        return new GoogleSearchConsoleStatus(
            Connected: true,
            LastSyncUtc: latestSync ?? connection.UpdatedAtUtc,
            SelectedProperty: string.IsNullOrWhiteSpace(connection.ExternalAccountId) ? null : connection.ExternalAccountId,
            AuthScopeState: "connected",
            HasCachedData: hasCache);
    }

    public async Task<IReadOnlyList<string>> GetPropertiesAsync(string ownerUserId, Guid? companyId, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(ownerUserId, companyId, cancellationToken);
        if (connection is null || string.IsNullOrWhiteSpace(connection.AccessTokenEncrypted))
        {
            return Array.Empty<string>();
        }
        if (!await EnsureValidAccessTokenAsync(connection, cancellationToken))
        {
            return Array.Empty<string>();
        }

        try
        {
            var client = _httpClientFactory.CreateClient("gsc-api");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessTokenEncrypted);
            var response = await client.GetAsync("https://www.googleapis.com/webmasters/v3/sites", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<string>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<JsonElement>(stream, cancellationToken: cancellationToken);
            if (!payload.TryGetProperty("siteEntry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return entries.EnumerateArray()
                .Select(x => x.TryGetProperty("siteUrl", out var url) ? url.GetString() : null)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList()!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load GSC properties for user {OwnerUserId}", ownerUserId);
            return Array.Empty<string>();
        }
    }

    public async Task SetSelectedPropertyAsync(string ownerUserId, Guid? companyId, int projectId, string propertyUri, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(ownerUserId, companyId, cancellationToken);
        if (connection is null)
        {
            return;
        }
        connection.ExternalAccountId = (propertyUri ?? string.Empty).Trim();
        connection.UpdatedAtUtc = DateTime.UtcNow;

        var metadata = ParseMetadata(connection.MetadataJson);
        metadata[$"project:{projectId}:selectedProperty"] = connection.ExternalAccountId;
        connection.MetadataJson = JsonSerializer.Serialize(metadata);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SyncAsync(string ownerUserId, Guid? companyId, int projectId, CancellationToken cancellationToken = default)
    {
        await EnsureSeoTablesAsync(cancellationToken);
        var connection = await GetConnectionAsync(ownerUserId, companyId, cancellationToken);
        if (connection is null || string.IsNullOrWhiteSpace(connection.AccessTokenEncrypted))
        {
            return false;
        }
        if (!await EnsureValidAccessTokenAsync(connection, cancellationToken))
        {
            return false;
        }

        var selectedProperty = ResolveSelectedProperty(connection, projectId);
        if (string.IsNullOrWhiteSpace(selectedProperty))
        {
            return false;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("gsc-api");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessTokenEncrypted);

            var encodedProperty = Uri.EscapeDataString(selectedProperty);
            var sitemapsResp = await client.GetAsync($"https://www.googleapis.com/webmasters/v3/sites/{encodedProperty}/sitemaps", cancellationToken);
            var sitemapsJson = await sitemapsResp.Content.ReadAsStringAsync(cancellationToken);
            var sitemapsPayload = sitemapsResp.IsSuccessStatusCode ? JsonSerializer.Deserialize<JsonElement>(sitemapsJson) : default;

            var end = DateTime.UtcNow.Date.AddDays(-1);
            var start = end.AddDays(-27);
            var searchReq = JsonSerializer.Serialize(new
            {
                startDate = start.ToString("yyyy-MM-dd"),
                endDate = end.ToString("yyyy-MM-dd"),
                dimensions = new[] { "query" },
                rowLimit = 50
            });
            using var searchResp = await client.PostAsync(
                $"https://www.googleapis.com/webmasters/v3/sites/{encodedProperty}/searchAnalytics/query",
                new StringContent(searchReq, System.Text.Encoding.UTF8, "application/json"),
                cancellationToken);
            var searchJson = await searchResp.Content.ReadAsStringAsync(cancellationToken);
            var searchPayload = searchResp.IsSuccessStatusCode ? JsonSerializer.Deserialize<JsonElement>(searchJson) : default;

            await UpsertSnapshotAsync(ownerUserId, companyId, projectId, selectedProperty, "sitemaps", sitemapsPayload, cancellationToken);
            await UpsertSnapshotAsync(ownerUserId, companyId, projectId, selectedProperty, "search_performance", searchPayload, cancellationToken);
            await UpsertSnapshotAsync(ownerUserId, companyId, projectId, selectedProperty, "issues", BuildIssuesPayloadPlaceholder(), cancellationToken);
            connection.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GSC sync failed for user {OwnerUserId}, project {ProjectId}", ownerUserId, projectId);
            return false;
        }
    }

    public async Task<object?> GetLatestSnapshotAsync(string ownerUserId, Guid? companyId, int projectId, string snapshotType, CancellationToken cancellationToken = default)
    {
        await EnsureSeoTablesAsync(cancellationToken);
        var query = _db.AnalyticsSeoSnapshots.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId && x.ProjectId == projectId && x.SnapshotType == snapshotType);
        if (companyId.HasValue)
        {
            query = query.Where(x => x.CompanyId == companyId.Value);
        }
        var row = await query.OrderByDescending(x => x.CapturedAtUtc).FirstOrDefaultAsync(cancellationToken);
        if (row is null || string.IsNullOrWhiteSpace(row.PayloadJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(row.PayloadJson);
        }
        catch
        {
            return null;
        }
    }

    private async Task<IntegrationConnection?> GetConnectionAsync(string ownerUserId, Guid? companyId, CancellationToken cancellationToken)
    {
        var query = _db.IntegrationConnections.Where(x => x.Provider == "google_search_console" && x.OwnerUserId == ownerUserId);
        if (companyId.HasValue)
        {
            query = query.Where(x => x.CompanyId == companyId.Value);
        }
        return await query.OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> EnsureValidAccessTokenAsync(IntegrationConnection connection, CancellationToken cancellationToken)
    {
        if (!connection.ExpiresAtUtc.HasValue || connection.ExpiresAtUtc.Value > DateTime.UtcNow.AddMinutes(2))
        {
            return !string.IsNullOrWhiteSpace(connection.AccessTokenEncrypted);
        }
        if (string.IsNullOrWhiteSpace(connection.RefreshTokenEncrypted))
        {
            return false;
        }

        var clientId = _configuration["Authentication:Google:ClientId"];
        var clientSecret = _configuration["Authentication:Google:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return false;
        }

        try
        {
            var body = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["refresh_token"] = connection.RefreshTokenEncrypted,
                ["grant_type"] = "refresh_token"
            };
            var client = _httpClientFactory.CreateClient("gsc-api");
            using var resp = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(body), cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                connection.Status = "disconnected";
                connection.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                return false;
            }
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            var payload = JsonSerializer.Deserialize<JsonElement>(json);
            if (payload.TryGetProperty("access_token", out var token))
            {
                connection.AccessTokenEncrypted = token.GetString();
            }
            if (payload.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds))
            {
                connection.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds));
            }
            connection.Status = "connected";
            connection.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return !string.IsNullOrWhiteSpace(connection.AccessTokenEncrypted);
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<string, string> ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? ResolveSelectedProperty(IntegrationConnection connection, int projectId)
    {
        if (!string.IsNullOrWhiteSpace(connection.ExternalAccountId))
        {
            return connection.ExternalAccountId;
        }
        var metadata = ParseMetadata(connection.MetadataJson);
        return metadata.TryGetValue($"project:{projectId}:selectedProperty", out var projectSpecific) ? projectSpecific : null;
    }

    private async Task UpsertSnapshotAsync(
        string ownerUserId,
        Guid? companyId,
        int projectId,
        string propertyUri,
        string snapshotType,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        var row = new AnalyticsSeoSnapshot
        {
            ProjectId = projectId,
            OwnerUserId = ownerUserId,
            CompanyId = companyId,
            PropertyUri = propertyUri,
            SnapshotType = snapshotType,
            PayloadJson = payload.ValueKind == JsonValueKind.Undefined ? "{}" : payload.GetRawText(),
            CapturedAtUtc = DateTime.UtcNow
        };
        _db.AnalyticsSeoSnapshots.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static JsonElement BuildIssuesPayloadPlaceholder()
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            note = "Google Search Console URL inspection indexing details are not available through this API scope yet.",
            rows = Array.Empty<object>()
        });
        return payload;
    }

    private async Task EnsureSeoTablesAsync(CancellationToken cancellationToken)
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS AnalyticsSeoSnapshots (
    Id TEXT NOT NULL PRIMARY KEY,
    ProjectId INTEGER NOT NULL,
    OwnerUserId TEXT NOT NULL,
    CompanyId TEXT NULL,
    PropertyUri TEXT NOT NULL,
    SnapshotType TEXT NOT NULL,
    PayloadJson TEXT NOT NULL,
    CapturedAtUtc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_AnalyticsSeoSnapshots_Scope ON AnalyticsSeoSnapshots(OwnerUserId, CompanyId, ProjectId, SnapshotType, CapturedAtUtc);");
        }
        else
        {
            await _db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID('dbo.AnalyticsSeoSnapshots','U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AnalyticsSeoSnapshots](
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        [ProjectId] INT NOT NULL,
        [OwnerUserId] NVARCHAR(450) NOT NULL,
        [CompanyId] UNIQUEIDENTIFIER NULL,
        [PropertyUri] NVARCHAR(512) NOT NULL,
        [SnapshotType] NVARCHAR(32) NOT NULL,
        [PayloadJson] NVARCHAR(MAX) NOT NULL,
        [CapturedAtUtc] DATETIME2 NOT NULL
    );
END
IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name='IX_AnalyticsSeoSnapshots_Scope')
    CREATE INDEX IX_AnalyticsSeoSnapshots_Scope ON AnalyticsSeoSnapshots(OwnerUserId, CompanyId, ProjectId, SnapshotType, CapturedAtUtc);");
        }
    }
}
