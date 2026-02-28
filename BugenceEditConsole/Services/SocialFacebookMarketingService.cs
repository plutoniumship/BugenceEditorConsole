using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public sealed class SocialFacebookMarketingService
{
    private readonly ApplicationDbContext _db;
    private readonly MetaGraphClient _metaGraphClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly WorkflowExecutionService _workflowExecutionService;
    private readonly LeadMappingService _leadMappingService;
    private readonly TriggerRoutingService _triggerRoutingService;
    private readonly ILogger<SocialFacebookMarketingService> _logger;

    public SocialFacebookMarketingService(
        ApplicationDbContext db,
        MetaGraphClient metaGraphClient,
        UserManager<ApplicationUser> userManager,
        WorkflowExecutionService workflowExecutionService,
        LeadMappingService leadMappingService,
        TriggerRoutingService triggerRoutingService,
        ILogger<SocialFacebookMarketingService> logger)
    {
        _db = db;
        _metaGraphClient = metaGraphClient;
        _userManager = userManager;
        _workflowExecutionService = workflowExecutionService;
        _leadMappingService = leadMappingService;
        _triggerRoutingService = triggerRoutingService;
        _logger = logger;
    }

    public static (Guid TenantId, Guid WorkspaceId) ResolveTenantWorkspace(ApplicationUser user)
    {
        var baseId = user.CompanyId ?? StableGuid(user.Id);
        return (baseId, baseId);
    }

    public async Task<object> SyncAdsAsync(ApplicationUser user, Guid integrationConnectionId, CancellationToken cancellationToken)
    {
        await SocialMarketingSchemaService.EnsureSchemaAsync(_db);
        var (tenantId, workspaceId) = ResolveTenantWorkspace(user);
        var start = DateTime.UtcNow;
        var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var pages = await _metaGraphClient.GetAssetsAsync(user, "page", null, cancellationToken);
        var adAccounts = await _metaGraphClient.GetAssetsAsync(user, "ad_account", null, cancellationToken);

        foreach (var page in pages)
        {
            await UpsertPageAsync(tenantId, workspaceId, integrationConnectionId, page, cancellationToken);
            var forms = await _metaGraphClient.GetAssetsAsync(user, "form", page.Id, cancellationToken);
            foreach (var form in forms)
            {
                await UpsertFormAsync(tenantId, workspaceId, integrationConnectionId, page.Id, form, cancellationToken);
                await UpsertAdFromFormAsync(tenantId, workspaceId, integrationConnectionId, page.Id, form, cancellationToken);
                stats["forms"] = stats.GetValueOrDefault("forms") + 1;
            }
            stats["pages"] = stats.GetValueOrDefault("pages") + 1;
        }

        foreach (var ad in adAccounts)
        {
            await UpsertCampaignAdsetAdAsync(tenantId, workspaceId, integrationConnectionId, ad, cancellationToken);
            stats["ad_accounts"] = stats.GetValueOrDefault("ad_accounts") + 1;
        }

        await LogSyncAsync(tenantId, workspaceId, integrationConnectionId, "ads_metadata", "success", start, stats, null, cancellationToken);
        return new { success = true, counts = stats };
    }

    public async Task<object> SyncLeadsAsync(ApplicationUser user, Guid integrationConnectionId, int lookbackMinutes, CancellationToken cancellationToken)
    {
        await SocialMarketingSchemaService.EnsureSchemaAsync(_db);
        var (tenantId, workspaceId) = ResolveTenantWorkspace(user);
        var start = DateTime.UtcNow;
        var stats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var forms = await _db.MktMetaForms
            .Where(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId)
            .Take(200)
            .ToListAsync(cancellationToken);
        if (forms.Count == 0)
        {
            await SyncAdsAsync(user, integrationConnectionId, cancellationToken);
            forms = await _db.MktMetaForms
                .Where(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId)
                .Take(200)
                .ToListAsync(cancellationToken);
        }

        foreach (var form in forms)
        {
            var leadId = $"poll_{form.FormId}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var payload = await _metaGraphClient.GetLeadPayloadAsync(user, leadId, cancellationToken);
            payload.FormId ??= form.FormId;
            payload.PageId ??= form.PageId;
            var row = await UpsertLeadAsync(tenantId, workspaceId, integrationConnectionId, payload, "polling", cancellationToken);
            await ProcessOperationalRoutingAsync(user, row, integrationConnectionId, cancellationToken);
            stats["inserted"] = stats.GetValueOrDefault("inserted") + 1;
        }

        await UpsertSyncStateAsync(tenantId, workspaceId, integrationConnectionId, "leads_polling", DateTime.UtcNow, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), cancellationToken);
        await CheckAndProcessSlaBreachesAsync(user, integrationConnectionId, cancellationToken);
        await LogSyncAsync(tenantId, workspaceId, integrationConnectionId, "leads_polling", "success", start, stats, null, cancellationToken);
        return new { success = true, counts = stats, lookbackMinutes = lookbackMinutes <= 0 ? 15 : lookbackMinutes };
    }

    public async Task<object> IngestWebhookAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        await SocialMarketingSchemaService.EnsureSchemaAsync(_db);
        var inserted = 0;
        var duplicates = 0;

        if (payload.TryGetProperty("entry", out var entryNode) && entryNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entryNode.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changesNode) || changesNode.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                foreach (var change in changesNode.EnumerateArray())
                {
                    var field = change.TryGetProperty("field", out var fieldNode) ? fieldNode.GetString() : null;
                    if (!string.Equals(field, "leadgen", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var value = change.TryGetProperty("value", out var valueNode) ? valueNode : default;
                    var pageId = value.ValueKind != JsonValueKind.Undefined && value.TryGetProperty("page_id", out var pageNode) ? pageNode.GetString() : null;
                    var leadId = value.ValueKind != JsonValueKind.Undefined && value.TryGetProperty("leadgen_id", out var leadNode) ? leadNode.GetString() : null;
                    if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(leadId))
                    {
                        continue;
                    }

                    var page = await _db.MktMetaPages
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.PageId == pageId, cancellationToken);
                    if (page == null)
                    {
                        continue;
                    }
                    var connection = await _db.IntegrationConnections.FirstOrDefaultAsync(x => x.Id == page.IntegrationConnectionId, cancellationToken);
                    if (connection == null)
                    {
                        continue;
                    }
                    var user = await _userManager.FindByIdAsync(connection.OwnerUserId);
                    if (user == null)
                    {
                        continue;
                    }

                    try
                    {
                        var leadPayload = await _metaGraphClient.GetLeadPayloadAsync(user, leadId, cancellationToken);
                        leadPayload.PageId ??= pageId;
                        var row = await UpsertLeadAsync(page.TenantId, page.WorkspaceId, connection.Id, leadPayload, "webhook", cancellationToken);
                        if (row.CreatedAtUtc == row.UpdatedAtUtc) inserted++; else duplicates++;
                        await TriggerAutoRulesAsync(page.TenantId, page.WorkspaceId, connection.Id, "lead_received", row, user, cancellationToken);
                        await ProcessOperationalRoutingAsync(user, row, connection.Id, cancellationToken);
                        await CheckAndProcessSlaBreachesAsync(user, connection.Id, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        await _db.MktDeadLetterEvents.AddAsync(new MktDeadLetterEvent
                        {
                            TenantId = page.TenantId,
                            WorkspaceId = page.WorkspaceId,
                            IntegrationConnectionId = connection.Id,
                            EventType = "meta_lead_webhook",
                            PayloadJson = payload.GetRawText(),
                            ErrorJson = JsonSerializer.Serialize(new { message = ex.Message }),
                            Attempts = 1,
                            CreatedAtUtc = DateTime.UtcNow
                        }, cancellationToken);
                    }
                }
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return new { success = true, inserted, duplicates };
    }

    public async Task<object> GetOpsSnapshotAsync(ApplicationUser user, Guid integrationConnectionId, CancellationToken cancellationToken)
    {
        await SocialMarketingSchemaService.EnsureSchemaAsync(_db);
        var (tenantId, workspaceId) = ResolveTenantWorkspace(user);
        var settings = await LoadSettingsAsync(user, integrationConnectionId, cancellationToken);
        var now = DateTime.UtcNow;
        var leads = await _db.MktMetaLeads.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId)
            .ToListAsync(cancellationToken);
        var syncLogs = await _db.MktSyncLogs.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId)
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(12)
            .ToListAsync(cancellationToken);
        var deadLetters = await _db.MktDeadLetterEvents.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId && x.ResolvedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);

        var slaMinutes = Math.Max(1, settings.ResponseSlaMinutes);
        var slaBreaches = leads.Count(x =>
            x.Status == "new" &&
            !x.LastActionAtUtc.HasValue &&
            x.CreatedTime.UtcDateTime <= now.AddMinutes(-slaMinutes));

        return new
        {
            success = true,
            health = new
            {
                connectionStatus = "connected",
                webhookEnabled = settings.WebhookEnabled,
                pollingIntervalMinutes = settings.PollingIntervalMinutes,
                responseSlaMinutes = settings.ResponseSlaMinutes,
                requireConsent = settings.RequireConsent,
                openDeadLetters = deadLetters.Count,
                recentSyncFailures = syncLogs.Count(x => !string.Equals(x.Status, "success", StringComparison.OrdinalIgnoreCase)),
                slaBreaches,
                leadsTracked = leads.Count
            }
        };
    }

    public async Task<IReadOnlyList<MktSyncLog>> GetSyncLogsAsync(ApplicationUser user, Guid integrationConnectionId, CancellationToken cancellationToken)
    {
        await SocialMarketingSchemaService.EnsureSchemaAsync(_db);
        var (tenantId, workspaceId) = ResolveTenantWorkspace(user);
        return await _db.MktSyncLogs.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId)
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MktDeadLetterEvent>> GetDeadLettersAsync(ApplicationUser user, Guid integrationConnectionId, CancellationToken cancellationToken)
    {
        await SocialMarketingSchemaService.EnsureSchemaAsync(_db);
        var (tenantId, workspaceId) = ResolveTenantWorkspace(user);
        return await _db.MktDeadLetterEvents
            .Where(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);
    }

    public async Task<object> RetryDeadLetterAsync(ApplicationUser user, Guid deadLetterId, CancellationToken cancellationToken)
    {
        await SocialMarketingSchemaService.EnsureSchemaAsync(_db);
        var (tenantId, workspaceId) = ResolveTenantWorkspace(user);
        var row = await _db.MktDeadLetterEvents.FirstOrDefaultAsync(x => x.Id == deadLetterId && x.TenantId == tenantId && x.WorkspaceId == workspaceId, cancellationToken);
        if (row == null)
        {
            return new { success = false, message = "Dead-letter event not found." };
        }

        row.Attempts += 1;
        try
        {
            using var doc = JsonDocument.Parse(row.PayloadJson);
            await IngestWebhookAsync(doc.RootElement, cancellationToken);
            row.ResolvedAtUtc = DateTime.UtcNow;
            row.ErrorJson = JsonSerializer.Serialize(new { message = "Retried successfully." });
            await _db.SaveChangesAsync(cancellationToken);
            return new { success = true, id = row.Id, resolvedAtUtc = row.ResolvedAtUtc };
        }
        catch (Exception ex)
        {
            row.ErrorJson = JsonSerializer.Serialize(new { message = ex.Message });
            await _db.SaveChangesAsync(cancellationToken);
            return new { success = false, message = ex.Message };
        }
    }

    public async Task<object> GetReportingSnapshotAsync(
        ApplicationUser user,
        Guid integrationConnectionId,
        DateTimeOffset? dateFrom,
        DateTimeOffset? dateTo,
        CancellationToken cancellationToken)
    {
        await SocialMarketingSchemaService.EnsureSchemaAsync(_db);
        var (tenantId, workspaceId) = ResolveTenantWorkspace(user);
        var from = dateFrom ?? DateTimeOffset.UtcNow.AddDays(-30);
        var to = dateTo ?? DateTimeOffset.UtcNow;

        var ads = await _db.MktMetaAds.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId)
            .ToListAsync(cancellationToken);
        var leads = await _db.MktMetaLeads.AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId &&
                x.WorkspaceId == workspaceId &&
                x.IntegrationConnectionId == integrationConnectionId &&
                x.CreatedTime >= from &&
                x.CreatedTime <= to)
            .ToListAsync(cancellationToken);
        var activities = await _db.MktLeadActivities.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(1000)
            .ToListAsync(cancellationToken);
        var workflowRuns = await _db.MktWorkflowRuns.AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId &&
                x.WorkspaceId == workspaceId &&
                x.CreatedAtUtc >= from.UtcDateTime &&
                x.CreatedAtUtc <= to.UtcDateTime)
            .ToListAsync(cancellationToken);

        var total = leads.Count;
        var contacted = leads.Count(x => x.Status is "contacted" or "responded" or "qualified" or "booked");
        var qualified = leads.Count(x => x.Status is "qualified" or "booked");
        var booked = leads.Count(x => x.Status == "booked");
        var duplicateCount = leads.Count(x => x.DuplicateOfLeadId != null);
        var avgResponseMinutes = leads
            .Where(x => x.LastActionAtUtc.HasValue)
            .Select(x => Math.Max(0, (x.LastActionAtUtc!.Value - x.CreatedTime.UtcDateTime).TotalMinutes))
            .DefaultIfEmpty(0)
            .Average();

        var topAds = ads.Select(ad =>
        {
            var adLeads = leads.Where(x => x.AdId == ad.AdId).ToList();
            var adTotal = adLeads.Count;
            var adQualified = adLeads.Count(x => x.Status is "qualified" or "booked");
            var adBooked = adLeads.Count(x => x.Status == "booked");
            return new
            {
                adId = ad.AdId,
                adName = ad.Name,
                campaignId = ad.CampaignId,
                adsetId = ad.AdsetId,
                formId = ad.FormId,
                platform = ad.Platform,
                leads = adTotal,
                qualifiedRate = adTotal == 0 ? 0m : Math.Round(adQualified * 100m / adTotal, 2),
                bookedRate = adTotal == 0 ? 0m : Math.Round(adBooked * 100m / adTotal, 2),
                avgScore = Math.Round(adLeads.Where(x => x.Score.HasValue).Select(x => (decimal)x.Score!.Value).DefaultIfEmpty(0).Average(), 2),
                lastLeadAtUtc = adLeads.MaxBy(x => x.CreatedTime)?.CreatedTime ?? ad.LastLeadAtUtc
            };
        })
        .Where(x => x.leads > 0)
        .OrderByDescending(x => x.leads)
        .ThenByDescending(x => x.qualifiedRate)
        .Take(8)
        .ToList();

        var trend = Enumerable.Range(0, Math.Max(1, (to.UtcDateTime.Date - from.UtcDateTime.Date).Days + 1))
            .Select(offset =>
            {
                var day = from.UtcDateTime.Date.AddDays(offset);
                var dayLeads = leads.Where(x => x.CreatedTime.UtcDateTime.Date == day).ToList();
                return new
                {
                    day = day.ToString("yyyy-MM-dd"),
                    leads = dayLeads.Count,
                    qualified = dayLeads.Count(x => x.Status is "qualified" or "booked"),
                    booked = dayLeads.Count(x => x.Status == "booked")
                };
            })
            .ToList();

        var stageBreakdown = leads
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Status) ? "new" : x.Status)
            .OrderByDescending(x => x.Count())
            .Select(x => new { stage = x.Key, count = x.Count() })
            .ToList();

        var ownerIds = leads.Where(x => !string.IsNullOrWhiteSpace(x.AssignedUserId)).Select(x => x.AssignedUserId!).Distinct().ToList();
        var ownerMap = ownerIds.Count == 0
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : await _userManager.Users
                .Where(x => ownerIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.DisplayName) ? (x.UserName ?? x.Email ?? x.Id) : x.DisplayName!, cancellationToken);

        var agentLeaderboard = leads
            .Where(x => !string.IsNullOrWhiteSpace(x.AssignedUserId))
            .GroupBy(x => x.AssignedUserId!)
            .Select(x => new
            {
                assignedUserId = x.Key,
                owner = ownerMap.TryGetValue(x.Key, out var ownerName) ? ownerName : x.Key,
                leads = x.Count(),
                qualified = x.Count(y => y.Status is "qualified" or "booked"),
                booked = x.Count(y => y.Status == "booked"),
                avgResponseMinutes = Math.Round(x.Where(y => y.LastActionAtUtc.HasValue)
                    .Select(y => Math.Max(0, (y.LastActionAtUtc!.Value - y.CreatedTime.UtcDateTime).TotalMinutes))
                    .DefaultIfEmpty(0)
                    .Average(), 2)
            })
            .OrderByDescending(x => x.booked)
            .ThenByDescending(x => x.qualified)
            .ThenByDescending(x => x.leads)
            .Take(8)
            .ToList();

        var automationSummary = workflowRuns
            .GroupBy(x => x.Status)
            .Select(x => new { status = x.Key, count = x.Count() })
            .ToList();

        var recentTimeline = activities.Take(14).Select(x => new
        {
            id = x.Id,
            type = x.Type,
            leadPkId = x.LeadPkId,
            createdBy = x.CreatedBy,
            createdAtUtc = x.CreatedAtUtc
        }).ToList();

        return new
        {
            success = true,
            overview = new
            {
                totalLeads = total,
                contactedRate = total == 0 ? 0m : Math.Round(contacted * 100m / total, 2),
                qualifiedRate = total == 0 ? 0m : Math.Round(qualified * 100m / total, 2),
                bookedRate = total == 0 ? 0m : Math.Round(booked * 100m / total, 2),
                duplicateRate = total == 0 ? 0m : Math.Round(duplicateCount * 100m / total, 2),
                avgResponseMinutes = Math.Round(avgResponseMinutes, 2),
                automationsRun = workflowRuns.Count
            },
            trend,
            stageBreakdown,
            topAds,
            agentLeaderboard,
            automationSummary,
            recentTimeline
        };
    }

    public async Task<LeadContext> BuildLeadContextAsync(MktMetaLead lead, ApplicationUser user, string mode, string eventType, CancellationToken cancellationToken)
    {
        var (tenantId, workspaceId) = ResolveTenantWorkspace(user);
        var page = lead.PageId == null ? null : await _db.MktMetaPages.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.PageId == lead.PageId, cancellationToken);
        var form = lead.FormId == null ? null : await _db.MktMetaForms.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.FormId == lead.FormId, cancellationToken);
        var campaign = lead.CampaignId == null ? null : await _db.MktMetaCampaigns.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.CampaignId == lead.CampaignId, cancellationToken);
        var adset = lead.AdsetId == null ? null : await _db.MktMetaAdsets.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.AdsetId == lead.AdsetId, cancellationToken);
        var ad = lead.AdId == null ? null : await _db.MktMetaAds.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.AdId == lead.AdId, cancellationToken);

        Dictionary<string, string?> fields;
        try
        {
            fields = JsonSerializer.Deserialize<Dictionary<string, string?>>(lead.FieldDataJson) ?? new Dictionary<string, string?>();
        }
        catch
        {
            fields = new Dictionary<string, string?>();
        }

        var answers = fields.Select(f => new LeadAnswer
        {
            Key = f.Key,
            Label = f.Key.Replace("_", " ", StringComparison.OrdinalIgnoreCase),
            Type = GuessType(f.Key),
            Value = f.Value
        }).ToList();

        var ctx = new LeadContext
        {
            Source = new LeadContextSource
            {
                Channel = "facebook",
                Platform = lead.Platform,
                Provider = "meta",
                IntegrationConnectionId = lead.IntegrationConnectionId.ToString(),
                ReceivedAt = DateTimeOffset.UtcNow,
                EventType = eventType,
                Mode = mode
            },
            Attribution = new LeadContextAttribution
            {
                Page = new LeadRef { Id = lead.PageId ?? string.Empty, Name = page?.Name ?? string.Empty },
                Form = new LeadRef { Id = lead.FormId ?? string.Empty, Name = form?.Name ?? string.Empty },
                Campaign = new LeadRef { Id = lead.CampaignId ?? string.Empty, Name = campaign?.Name ?? string.Empty },
                Adset = new LeadRef { Id = lead.AdsetId ?? string.Empty, Name = adset?.Name ?? string.Empty },
                Ad = new LeadRef { Id = lead.AdId ?? string.Empty, Name = ad?.Name ?? string.Empty },
                Utm = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["source"] = "facebook",
                    ["medium"] = "paid_social",
                    ["campaign"] = campaign?.Name,
                    ["content"] = ad?.Name,
                    ["term"] = adset?.Name
                }
            },
            Lead = new LeadContextLead
            {
                ProviderLeadId = lead.LeadId,
                CreatedTime = lead.CreatedTime,
                Consent = new LeadConsent
                {
                    HasConsent = lead.ConsentJson.Contains("true", StringComparison.OrdinalIgnoreCase),
                    Raw = new Dictionary<string, object?> { ["json"] = lead.ConsentJson }
                },
                Identity = new LeadIdentity
                {
                    FullName = lead.NormalizedName,
                    Email = lead.NormalizedEmail,
                    Phone = lead.NormalizedPhone,
                    Country = lead.Country,
                    City = lead.City
                },
                Answers = answers
            },
            Bugence = new LeadContextBugence
            {
                TenantId = tenantId.ToString(),
                WorkspaceId = workspaceId.ToString(),
                MarketingLeadId = lead.Id.ToString(),
                Crm = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["leadId"] = null,
                    ["contactId"] = null,
                    ["dealId"] = null
                },
                Owner = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["assignedUserId"] = lead.AssignedUserId,
                    ["assignedRule"] = null
                },
                Status = lead.Status,
                Score = lead.Score
            }
        };

        if (!string.IsNullOrWhiteSpace(lead.RawJson))
        {
            ctx.Lead.Raw["payload"] = lead.RawJson;
        }

        return ctx;
    }

    public async Task<ActionResultContract> AddLeadActivityAsync(MktMetaLead lead, Guid tenantId, Guid workspaceId, string activityType, object payload, string createdBy, CancellationToken cancellationToken, bool updateLastAction = true)
    {
        var activity = new MktLeadActivity
        {
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            LeadPkId = lead.Id,
            Type = activityType,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedBy = createdBy,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.MktLeadActivities.Add(activity);
        if (updateLastAction)
        {
            lead.LastActionAtUtc = DateTime.UtcNow;
        }
        lead.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return new ActionResultContract
        {
            Ok = true,
            Action = activityType,
            LeadId = lead.Id.ToString(),
            LoggedActivityId = activity.Id.ToString(),
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private async Task UpsertPageAsync(Guid tenantId, Guid workspaceId, Guid integrationConnectionId, FacebookAssetDto page, CancellationToken cancellationToken)
    {
        var row = await _db.MktMetaPages.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId && x.PageId == page.Id,
            cancellationToken);
        if (row == null)
        {
            row = new MktMetaPage
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                IntegrationConnectionId = integrationConnectionId,
                PageId = page.Id
            };
            _db.MktMetaPages.Add(row);
        }
        row.Name = page.Name;
        row.RawJson = JsonSerializer.Serialize(page);
        row.SyncedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertFormAsync(Guid tenantId, Guid workspaceId, Guid integrationConnectionId, string pageId, FacebookAssetDto form, CancellationToken cancellationToken)
    {
        var row = await _db.MktMetaForms.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId && x.FormId == form.Id,
            cancellationToken);
        if (row == null)
        {
            row = new MktMetaForm
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                IntegrationConnectionId = integrationConnectionId,
                FormId = form.Id
            };
            _db.MktMetaForms.Add(row);
        }
        row.PageId = pageId;
        row.Name = form.Name;
        row.QuestionsJson = JsonSerializer.Serialize(new[] { new { key = "name", label = "Name" }, new { key = "email", label = "Email" } });
        row.SyncedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertAdFromFormAsync(Guid tenantId, Guid workspaceId, Guid integrationConnectionId, string pageId, FacebookAssetDto form, CancellationToken cancellationToken)
    {
        var adId = $"form_{form.Id}";
        var ad = await _db.MktMetaAds.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId && x.AdId == adId, cancellationToken);
        if (ad == null)
        {
            ad = new MktMetaAd
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                IntegrationConnectionId = integrationConnectionId,
                AdId = adId
            };
            _db.MktMetaAds.Add(ad);
        }
        ad.Name = $"Form: {form.Name}";
        ad.CampaignId = ad.CampaignId.Length == 0 ? "campaign_default" : ad.CampaignId;
        ad.AdsetId = ad.AdsetId.Length == 0 ? "adset_default" : ad.AdsetId;
        ad.Status = "active";
        ad.Platform = "fb";
        ad.PageId = pageId;
        ad.FormId = form.Id;
        ad.SyncedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertCampaignAdsetAdAsync(Guid tenantId, Guid workspaceId, Guid integrationConnectionId, FacebookAssetDto adAccount, CancellationToken cancellationToken)
    {
        var campaignId = $"campaign_{adAccount.Id}";
        var adsetId = $"adset_{adAccount.Id}";
        var adId = $"ad_{adAccount.Id}";

        var campaign = await _db.MktMetaCampaigns.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId && x.CampaignId == campaignId, cancellationToken);
        if (campaign == null)
        {
            campaign = new MktMetaCampaign
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                IntegrationConnectionId = integrationConnectionId,
                CampaignId = campaignId
            };
            _db.MktMetaCampaigns.Add(campaign);
        }
        campaign.Name = $"Campaign - {adAccount.Name}";
        campaign.Status = "active";
        campaign.SyncedAtUtc = DateTime.UtcNow;

        var adset = await _db.MktMetaAdsets.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId && x.AdsetId == adsetId, cancellationToken);
        if (adset == null)
        {
            adset = new MktMetaAdset
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                IntegrationConnectionId = integrationConnectionId,
                AdsetId = adsetId
            };
            _db.MktMetaAdsets.Add(adset);
        }
        adset.CampaignId = campaignId;
        adset.Name = $"Ad Set - {adAccount.Name}";
        adset.Status = "active";
        adset.SyncedAtUtc = DateTime.UtcNow;

        var ad = await _db.MktMetaAds.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId && x.AdId == adId, cancellationToken);
        if (ad == null)
        {
            ad = new MktMetaAd
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                IntegrationConnectionId = integrationConnectionId,
                AdId = adId
            };
            _db.MktMetaAds.Add(ad);
        }
        ad.CampaignId = campaignId;
        ad.AdsetId = adsetId;
        ad.Name = $"Ad - {adAccount.Name}";
        ad.Status = "active";
        ad.Platform = "fb";
        ad.SyncedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<MktMetaLead> UpsertLeadAsync(Guid tenantId, Guid workspaceId, Guid integrationConnectionId, FacebookLeadPayloadDto payload, string sourceMode, CancellationToken cancellationToken)
    {
        var existing = await _db.MktMetaLeads.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId && x.LeadId == payload.LeadId, cancellationToken);
        var now = DateTime.UtcNow;
        var fields = payload.FieldData ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var email = FirstField(fields, "email");
        var phone = FirstField(fields, "phone", "phone_number", "mobile");
        var name = FirstField(fields, "full_name", "name");
        var city = FirstField(fields, "city");
        var country = FirstField(fields, "country");

        if (existing == null)
        {
            existing = new MktMetaLead
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                IntegrationConnectionId = integrationConnectionId,
                LeadId = payload.LeadId ?? Guid.NewGuid().ToString("N"),
                DedupeKeyLead = payload.LeadId ?? Guid.NewGuid().ToString("N"),
                CreatedAtUtc = now
            };
            _db.MktMetaLeads.Add(existing);
        }

        existing.PageId = payload.PageId;
        existing.FormId = payload.FormId;
        existing.CampaignId = payload.CampaignId;
        existing.AdsetId = payload.AdsetId;
        existing.AdId = payload.AdId ?? existing.AdId ?? (payload.FormId == null ? null : $"form_{payload.FormId}");
        existing.Platform = (payload.Platform ?? "FB").ToLowerInvariant();
        existing.CreatedTime = payload.CreatedTime ?? DateTimeOffset.UtcNow;
        existing.FieldDataJson = JsonSerializer.Serialize(fields);
        existing.ConsentJson = JsonSerializer.Serialize(payload.ConsentFlags ?? new Dictionary<string, string?>());
        existing.NormalizedName = name;
        existing.NormalizedEmail = email?.Trim().ToLowerInvariant();
        existing.NormalizedPhone = NormalizePhone(phone);
        existing.Country = country;
        existing.City = city;
        existing.DedupeKeyEmail = existing.NormalizedEmail;
        existing.DedupeKeyPhone = existing.NormalizedPhone;
        existing.Score = CalculateLeadScore(fields, existing.NormalizedEmail, existing.NormalizedPhone, payload.ConsentFlags);
        existing.Status = ResolveLeadStatus(existing, payload);
        existing.RawJson = JsonSerializer.Serialize(payload);
        existing.UpdatedAtUtc = now;

        var duplicate = await FindDuplicateAsync(tenantId, workspaceId, integrationConnectionId, existing, cancellationToken);
        existing.DuplicateOfLeadId = duplicate?.Id;

        var ad = existing.AdId == null ? null : await _db.MktMetaAds.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId && x.AdId == existing.AdId, cancellationToken);
        if (ad != null)
        {
            ad.LastLeadAtUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await AddLeadActivityAsync(existing, tenantId, workspaceId, "captured", new { mode = sourceMode, leadId = existing.LeadId }, "system", cancellationToken, updateLastAction: false);
        if (duplicate != null)
        {
            await AddLeadActivityAsync(existing, tenantId, workspaceId, "duplicate_linked", new { duplicateOfLeadId = duplicate.Id }, "system", cancellationToken, updateLastAction: false);
        }
        return existing;
    }

    private async Task TriggerAutoRulesAsync(Guid tenantId, Guid workspaceId, Guid integrationConnectionId, string eventType, MktMetaLead lead, ApplicationUser ownerUser, CancellationToken cancellationToken)
    {
        var rules = await _db.MktAutoRules
            .Where(x =>
                x.TenantId == tenantId &&
                x.WorkspaceId == workspaceId &&
                x.IntegrationConnectionId == integrationConnectionId &&
                x.Channel == "facebook" &&
                x.EventType == eventType &&
                x.IsEnabled)
            .ToListAsync(cancellationToken);

        foreach (var rule in rules)
        {
            var workflow = await _db.Workflows.FirstOrDefaultAsync(x => x.Id == rule.WorkflowId && x.OwnerUserId == ownerUser.Id, cancellationToken);
            if (workflow == null)
            {
                continue;
            }

            var context = new WorkflowTriggerContext(
                Email: lead.NormalizedEmail,
                Fields: JsonSerializer.Deserialize<Dictionary<string, string?>>(lead.FieldDataJson) ?? new Dictionary<string, string?>(),
                SourceUrl: null,
                ElementTag: "socialMarketing",
                ElementId: lead.Id.ToString(),
                Provider: "facebook",
                BranchKey: "primary",
                RawPayloadJson: lead.RawJson,
                MappedFields: JsonSerializer.Deserialize<Dictionary<string, string?>>(lead.FieldDataJson) ?? new Dictionary<string, string?>(),
                ValidationFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    ["has_contact"] = !string.IsNullOrWhiteSpace(lead.NormalizedEmail) || !string.IsNullOrWhiteSpace(lead.NormalizedPhone),
                    ["has_email"] = !string.IsNullOrWhiteSpace(lead.NormalizedEmail),
                    ["has_phone"] = !string.IsNullOrWhiteSpace(lead.NormalizedPhone)
                });

            var run = new MktWorkflowRun
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                WorkflowId = workflow.Id,
                TriggerType = eventType,
                SourceEntity = "mkt_lead",
                SourceId = lead.Id,
                Status = "running",
                LogsJson = "{}",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _db.MktWorkflowRuns.Add(run);
            await _db.SaveChangesAsync(cancellationToken);

            try
            {
                var (ok, error) = await _workflowExecutionService.ExecuteAsync(workflow, context);
                run.Status = ok ? "success" : "fail";
                run.LogsJson = JsonSerializer.Serialize(new { ok, error });
                run.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);

                await AddLeadActivityAsync(lead, tenantId, workspaceId, "workflow_run", new
                {
                    workflowId = workflow.Id,
                    workflowName = workflow.Caption ?? workflow.Name,
                    workflowRunId = run.Id,
                    ok,
                    error
                }, "system", cancellationToken);
            }
            catch (Exception ex)
            {
                run.Status = "fail";
                run.LogsJson = JsonSerializer.Serialize(new { ok = false, error = ex.Message });
                run.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private async Task ProcessOperationalRoutingAsync(ApplicationUser user, MktMetaLead lead, Guid integrationConnectionId, CancellationToken cancellationToken)
    {
        var (tenantId, workspaceId) = ResolveTenantWorkspace(user);
        var settings = await LoadSettingsAsync(user, integrationConnectionId, cancellationToken);
        var flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["has_contact"] = !string.IsNullOrWhiteSpace(lead.NormalizedEmail) || !string.IsNullOrWhiteSpace(lead.NormalizedPhone),
            ["has_email"] = !string.IsNullOrWhiteSpace(lead.NormalizedEmail),
            ["has_phone"] = !string.IsNullOrWhiteSpace(lead.NormalizedPhone),
            ["has_consent"] = lead.ConsentJson.Contains("true", StringComparison.OrdinalIgnoreCase)
        };
        var routing = _triggerRoutingService.Decide(
            lead.DuplicateOfLeadId.HasValue,
            new TriggerValidationConfigDto
            {
                RequireConsent = settings.RequireConsent,
                ReplayWindowMinutes = settings.PollingIntervalMinutes,
                RouteMissingContactToNeedsEnrichment = true
            },
            flags);

        if (routing.BranchKey == "needs_enrichment")
        {
            lead.Status = "needs_enrichment";
            await _db.SaveChangesAsync(cancellationToken);
            if (!await ActivityExistsAsync(lead.Id, tenantId, workspaceId, "needs_enrichment", cancellationToken))
            {
                await AddLeadActivityAsync(lead, tenantId, workspaceId, "needs_enrichment", new { reason = routing.Reason }, "system", cancellationToken, updateLastAction: false);
            }
            await TriggerAutoRulesAsync(tenantId, workspaceId, integrationConnectionId, "lead_needs_enrichment", lead, user, cancellationToken);
        }

        if (routing.BranchKey == "compliance_review")
        {
            if (!await ActivityExistsAsync(lead.Id, tenantId, workspaceId, "compliance_review", cancellationToken))
            {
                await AddLeadActivityAsync(lead, tenantId, workspaceId, "compliance_review", new { reason = routing.Reason }, "system", cancellationToken, updateLastAction: false);
            }
        }

        if ((lead.Score ?? 0) >= 80)
        {
            if (!await ActivityExistsAsync(lead.Id, tenantId, workspaceId, "lead_marked_hot", cancellationToken))
            {
                await AddLeadActivityAsync(lead, tenantId, workspaceId, "lead_marked_hot", new { score = lead.Score }, "system", cancellationToken, updateLastAction: false);
                await TriggerAutoRulesAsync(tenantId, workspaceId, integrationConnectionId, "lead_marked_hot", lead, user, cancellationToken);
            }
        }
    }

    public async Task CheckAndProcessSlaBreachesAsync(ApplicationUser user, Guid integrationConnectionId, CancellationToken cancellationToken)
    {
        var (tenantId, workspaceId) = ResolveTenantWorkspace(user);
        var settings = await LoadSettingsAsync(user, integrationConnectionId, cancellationToken);
        var thresholdUtc = DateTime.UtcNow.AddMinutes(-Math.Max(1, settings.ResponseSlaMinutes));
        var leads = await _db.MktMetaLeads
            .Where(x =>
                x.TenantId == tenantId &&
                x.WorkspaceId == workspaceId &&
                x.IntegrationConnectionId == integrationConnectionId &&
                x.Status == "new" &&
                !x.LastActionAtUtc.HasValue &&
                x.CreatedTime.UtcDateTime <= thresholdUtc)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var lead in leads)
        {
            var alreadyLogged = await _db.MktLeadActivities.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.LeadPkId == lead.Id && x.Type == "sla_breached", cancellationToken);
            if (alreadyLogged)
            {
                continue;
            }

            await AddLeadActivityAsync(lead, tenantId, workspaceId, "sla_breached", new
            {
                responseSlaMinutes = settings.ResponseSlaMinutes,
                createdTime = lead.CreatedTime
            }, "system", cancellationToken, updateLastAction: false);
            await TriggerAutoRulesAsync(tenantId, workspaceId, integrationConnectionId, "sla_breached", lead, user, cancellationToken);
        }
    }

    private async Task<SocialFacebookSettingsDto> LoadSettingsAsync(ApplicationUser user, Guid integrationConnectionId, CancellationToken cancellationToken)
    {
        var connection = await _db.IntegrationConnections.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == integrationConnectionId && x.OwnerUserId == user.Id && x.Provider == "facebook", cancellationToken);
        if (connection == null || string.IsNullOrWhiteSpace(connection.MetadataJson))
        {
            return new SocialFacebookSettingsDto { IntegrationConnectionId = integrationConnectionId };
        }

        try
        {
            using var doc = JsonDocument.Parse(connection.MetadataJson);
            if (doc.RootElement.TryGetProperty("socialFacebookSettings", out var settingsNode))
            {
                var settings = JsonSerializer.Deserialize<SocialFacebookSettingsDto>(settingsNode.GetRawText()) ?? new SocialFacebookSettingsDto();
                settings.IntegrationConnectionId = integrationConnectionId;
                return settings;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to parse social Facebook settings metadata for {ConnectionId}", integrationConnectionId);
        }

        return new SocialFacebookSettingsDto { IntegrationConnectionId = integrationConnectionId };
    }

    private async Task UpsertSyncStateAsync(Guid tenantId, Guid workspaceId, Guid integrationConnectionId, string scope, DateTime lastSuccessAtUtc, string? cursor, CancellationToken cancellationToken)
    {
        var row = await _db.MktSyncStates.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId &&
            x.WorkspaceId == workspaceId &&
            x.IntegrationConnectionId == integrationConnectionId &&
            x.Scope == scope, cancellationToken);
        if (row == null)
        {
            row = new MktSyncState
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                IntegrationConnectionId = integrationConnectionId,
                Scope = scope
            };
            _db.MktSyncStates.Add(row);
        }

        row.LastSuccessAtUtc = lastSuccessAtUtc;
        row.LastCursor = cursor;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task LogSyncAsync(Guid tenantId, Guid workspaceId, Guid integrationConnectionId, string scope, string status, DateTime startedAtUtc, Dictionary<string, int> counts, string? error, CancellationToken cancellationToken)
    {
        var log = new MktSyncLog
        {
            TenantId = tenantId,
            WorkspaceId = workspaceId,
            IntegrationConnectionId = integrationConnectionId,
            Scope = scope,
            Status = status,
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = DateTime.UtcNow,
            CountsJson = JsonSerializer.Serialize(counts),
            ErrorJson = error
        };
        _db.MktSyncLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string? FirstField(IDictionary<string, string?> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return null;
    }

    private static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var cleaned = new string(value.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (cleaned.StartsWith("00", StringComparison.Ordinal))
        {
            cleaned = "+" + cleaned[2..];
        }
        if (!cleaned.StartsWith("+", StringComparison.Ordinal) && cleaned.Length == 10)
        {
            cleaned = "+1" + cleaned;
        }
        return cleaned;
    }

    private static string GuessType(string key)
    {
        var normalized = key.Trim().ToLowerInvariant();
        if (normalized.Contains("email", StringComparison.Ordinal)) return "email";
        if (normalized.Contains("phone", StringComparison.Ordinal) || normalized.Contains("mobile", StringComparison.Ordinal)) return "phone";
        if (normalized.Contains("city", StringComparison.Ordinal) || normalized.Contains("country", StringComparison.Ordinal)) return "text";
        return "text";
    }

    private static Guid StableGuid(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }

    private static int CalculateLeadScore(IDictionary<string, string?> fields, string? email, string? phone, IDictionary<string, string?>? consent)
    {
        var score = 20;
        if (!string.IsNullOrWhiteSpace(email)) score += 20;
        if (!string.IsNullOrWhiteSpace(phone)) score += 20;
        if (consent != null && consent.Values.Any(v => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase))) score += 10;
        if (fields.Keys.Any(k => k.Contains("budget", StringComparison.OrdinalIgnoreCase))) score += 15;
        if (fields.Keys.Any(k => k.Contains("service", StringComparison.OrdinalIgnoreCase) || k.Contains("company", StringComparison.OrdinalIgnoreCase))) score += 15;
        return Math.Min(score, 100);
    }

    private static string ResolveLeadStatus(MktMetaLead lead, FacebookLeadPayloadDto payload)
    {
        var hasEmail = !string.IsNullOrWhiteSpace(lead.NormalizedEmail);
        var hasPhone = !string.IsNullOrWhiteSpace(lead.NormalizedPhone);
        if (!hasEmail && !hasPhone)
        {
            return "needs_enrichment";
        }

        if ((lead.Score ?? 0) >= 80)
        {
            return "qualified";
        }

        return string.IsNullOrWhiteSpace(lead.Status) ? "new" : lead.Status;
    }

    private async Task<MktMetaLead?> FindDuplicateAsync(Guid tenantId, Guid workspaceId, Guid integrationConnectionId, MktMetaLead lead, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(lead.NormalizedEmail) && string.IsNullOrWhiteSpace(lead.NormalizedPhone))
        {
            return null;
        }

        return await _db.MktMetaLeads
            .Where(x =>
                x.TenantId == tenantId &&
                x.WorkspaceId == workspaceId &&
                x.IntegrationConnectionId == integrationConnectionId &&
                x.Id != lead.Id &&
                ((!string.IsNullOrWhiteSpace(lead.NormalizedEmail) && x.NormalizedEmail == lead.NormalizedEmail) ||
                 (!string.IsNullOrWhiteSpace(lead.NormalizedPhone) && x.NormalizedPhone == lead.NormalizedPhone)))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<bool> ActivityExistsAsync(Guid leadId, Guid tenantId, Guid workspaceId, string type, CancellationToken cancellationToken)
        => _db.MktLeadActivities.AsNoTracking().AnyAsync(x =>
            x.LeadPkId == leadId &&
            x.TenantId == tenantId &&
            x.WorkspaceId == workspaceId &&
            x.Type == type, cancellationToken);
}
