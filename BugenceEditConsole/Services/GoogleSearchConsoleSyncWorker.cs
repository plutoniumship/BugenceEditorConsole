using System.Text.Json;
using BugenceEditConsole.Data;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public sealed class GoogleSearchConsoleSyncWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(20);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GoogleSearchConsoleSyncWorker> _logger;

    public GoogleSearchConsoleSyncWorker(IServiceScopeFactory scopeFactory, ILogger<GoogleSearchConsoleSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSyncCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GSC sync cycle failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunSyncCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IGoogleSearchConsoleService>();

        var connections = await db.IntegrationConnections.AsNoTracking()
            .Where(x => x.Provider == "google_search_console" && x.Status != "disconnected")
            .ToListAsync(cancellationToken);

        foreach (var connection in connections)
        {
            var targets = ParseProjectTargets(connection.MetadataJson);
            foreach (var projectId in targets)
            {
                try
                {
                    await service.SyncAsync(connection.OwnerUserId, connection.CompanyId, projectId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "GSC scheduled sync failed for user {OwnerUserId}, project {ProjectId}", connection.OwnerUserId, projectId);
                }
            }
        }
    }

    private static IReadOnlyList<int> ParseProjectTargets(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return Array.Empty<int>();
        }

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return map.Keys
                .Where(x => x.StartsWith("project:", StringComparison.OrdinalIgnoreCase) && x.EndsWith(":selectedProperty", StringComparison.OrdinalIgnoreCase))
                .Select(x =>
                {
                    var parts = x.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    return parts.Length >= 3 && int.TryParse(parts[1], out var projectId) ? projectId : 0;
                })
                .Where(x => x > 0)
                .Distinct()
                .ToList();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }
}

