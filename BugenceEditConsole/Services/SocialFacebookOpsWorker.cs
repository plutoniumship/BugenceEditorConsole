using System.Text.Json;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public sealed class SocialFacebookOpsWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SocialFacebookOpsWorker> _logger;

    public SocialFacebookOpsWorker(IServiceScopeFactory scopeFactory, ILogger<SocialFacebookOpsWorker> logger)
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
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Social Facebook ops cycle failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var socialService = scope.ServiceProvider.GetRequiredService<SocialFacebookMarketingService>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        await SocialMarketingSchemaService.EnsureSchemaAsync(db);

        var connections = await db.IntegrationConnections.AsNoTracking()
            .Where(x => x.Provider == "facebook" && x.Status != "disconnected")
            .ToListAsync(cancellationToken);

        foreach (var connection in connections)
        {
            var user = await userManager.FindByIdAsync(connection.OwnerUserId);
            if (user == null)
            {
                continue;
            }

            try
            {
                var settings = ParseSettings(connection.MetadataJson);
                if (await ShouldPollAsync(db, user, connection.Id, settings.PollingIntervalMinutes, cancellationToken))
                {
                    await socialService.SyncLeadsAsync(user, connection.Id, settings.PollingIntervalMinutes, cancellationToken);
                }

                await socialService.CheckAndProcessSlaBreachesAsync(user, connection.Id, cancellationToken);
                await RetryDeadLettersAsync(db, socialService, user, connection.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scheduled Facebook ops failed for connection {ConnectionId}", connection.Id);
                await notifications.AddAsync(connection.OwnerUserId, "Facebook ops warning", $"Scheduled Facebook sync failed for {connection.DisplayName}.", "warning", new { connectionId = connection.Id });
            }
        }
    }

    private static async Task<bool> ShouldPollAsync(ApplicationDbContext db, ApplicationUser user, Guid connectionId, int pollingIntervalMinutes, CancellationToken cancellationToken)
    {
        var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
        var intervalMinutes = Math.Max(5, pollingIntervalMinutes);
        var state = await db.MktSyncStates.AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantId &&
                x.WorkspaceId == workspaceId &&
                x.IntegrationConnectionId == connectionId &&
                x.Scope == "leads_polling", cancellationToken);
        if (state?.LastSuccessAtUtc == null)
        {
            return true;
        }

        return state.LastSuccessAtUtc.Value <= DateTime.UtcNow.AddMinutes(-intervalMinutes);
    }

    private static async Task RetryDeadLettersAsync(ApplicationDbContext db, SocialFacebookMarketingService socialService, ApplicationUser user, Guid connectionId, CancellationToken cancellationToken)
    {
        var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
        var rows = await db.MktDeadLetterEvents.AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId &&
                x.WorkspaceId == workspaceId &&
                x.IntegrationConnectionId == connectionId &&
                x.ResolvedAtUtc == null &&
                x.Attempts < 6)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (row.CreatedAtUtc > DateTime.UtcNow - ResolveBackoff(row.Attempts))
            {
                continue;
            }

            await socialService.RetryDeadLetterAsync(user, row.Id, cancellationToken);
        }
    }

    private static TimeSpan ResolveBackoff(int attempts)
        => attempts switch
        {
            <= 1 => TimeSpan.FromSeconds(5),
            2 => TimeSpan.FromSeconds(30),
            3 => TimeSpan.FromMinutes(2),
            _ => TimeSpan.FromMinutes(10)
        };

    private static SocialFacebookSettingsDto ParseSettings(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new SocialFacebookSettingsDto();
        }

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.TryGetProperty("socialFacebookSettings", out var node))
            {
                return JsonSerializer.Deserialize<SocialFacebookSettingsDto>(node.GetRawText()) ?? new SocialFacebookSettingsDto();
            }
        }
        catch
        {
            // ignore parse failures
        }

        return new SocialFacebookSettingsDto();
    }
}
