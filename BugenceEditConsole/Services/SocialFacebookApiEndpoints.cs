using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BugenceEditConsole.Data;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BugenceEditConsole.Services;

public static class SocialFacebookApiEndpoints
{
    public static IEndpointRouteBuilder MapSocialFacebookApi(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/social/facebook/sync/ads", async (
            SocialSyncAdsRequest request,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            SocialFacebookMarketingService socialService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            await SocialMarketingSchemaService.EnsureSchemaAsync(db);
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            if (request.IntegrationConnectionId == Guid.Empty)
            {
                return Results.BadRequest(new { success = false, message = "integrationConnectionId is required." });
            }

            var connection = await db.IntegrationConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.IntegrationConnectionId && x.OwnerUserId == user.Id && x.Provider == "facebook", cancellationToken);
            if (connection == null)
            {
                return Results.NotFound(new { success = false, message = "Facebook integration connection not found." });
            }

            var result = await socialService.SyncAdsAsync(user, request.IntegrationConnectionId, cancellationToken);
            return Results.Ok(result);
        }).RequireAuthorization().DisableAntiforgery();

        app.MapPost("/api/social/facebook/sync/leads", async (
            SocialSyncLeadsRequest request,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            SocialFacebookMarketingService socialService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            await SocialMarketingSchemaService.EnsureSchemaAsync(db);
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            if (request.IntegrationConnectionId == Guid.Empty)
            {
                return Results.BadRequest(new { success = false, message = "integrationConnectionId is required." });
            }

            var connection = await db.IntegrationConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.IntegrationConnectionId && x.OwnerUserId == user.Id && x.Provider == "facebook", cancellationToken);
            if (connection == null)
            {
                return Results.NotFound(new { success = false, message = "Facebook integration connection not found." });
            }

            var lookbackMinutes = request.LookbackMinutes <= 0 ? 15 : request.LookbackMinutes;
            var result = await socialService.SyncLeadsAsync(user, request.IntegrationConnectionId, lookbackMinutes, cancellationToken);
            return Results.Ok(result);
        }).RequireAuthorization().DisableAntiforgery();

        app.MapGet("/api/social/facebook/ads", async (
            Guid integrationConnectionId,
            DateTimeOffset? dateFrom,
            DateTimeOffset? dateTo,
            string? campaignId,
            string? adsetId,
            string? status,
            string? platform,
            string? formId,
            string? search,
            string? owner,
            int? page,
            int? pageSize,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            await SocialMarketingSchemaService.EnsureSchemaAsync(db);
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
            var connection = await db.IntegrationConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == integrationConnectionId && x.OwnerUserId == user.Id && x.Provider == "facebook", cancellationToken);
            if (connection == null)
            {
                return Results.NotFound(new { success = false, message = "Facebook integration connection not found." });
            }

            var from = dateFrom ?? DateTimeOffset.UtcNow.AddDays(-30);
            var to = dateTo ?? DateTimeOffset.UtcNow;

            var adsQuery = db.MktMetaAds.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId);

            if (!string.IsNullOrWhiteSpace(campaignId)) adsQuery = adsQuery.Where(x => x.CampaignId == campaignId.Trim());
            if (!string.IsNullOrWhiteSpace(adsetId)) adsQuery = adsQuery.Where(x => x.AdsetId == adsetId.Trim());
            if (!string.IsNullOrWhiteSpace(status)) adsQuery = adsQuery.Where(x => x.Status == status.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(platform)) adsQuery = adsQuery.Where(x => x.Platform == platform.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(formId)) adsQuery = adsQuery.Where(x => x.FormId == formId.Trim());
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLowerInvariant();
                adsQuery = adsQuery.Where(x =>
                    x.Name.ToLower().Contains(s) ||
                    x.CampaignId.ToLower().Contains(s) ||
                    x.AdsetId.ToLower().Contains(s) ||
                    (x.FormId ?? string.Empty).ToLower().Contains(s));
            }

            var ads = await adsQuery.OrderByDescending(x => x.LastLeadAtUtc).ThenBy(x => x.Name).ToListAsync(cancellationToken);
            var adIds = ads.Select(x => x.AdId).ToList();
            var leadsQuery = db.MktMetaLeads.AsNoTracking().Where(x =>
                x.TenantId == tenantId &&
                x.WorkspaceId == workspaceId &&
                x.IntegrationConnectionId == integrationConnectionId &&
                x.AdId != null &&
                adIds.Contains(x.AdId) &&
                x.CreatedTime >= from &&
                x.CreatedTime <= to);
            if (!string.IsNullOrWhiteSpace(owner)) leadsQuery = leadsQuery.Where(x => x.AssignedUserId == owner.Trim());

            var leads = await leadsQuery.ToListAsync(cancellationToken);
            var rows = ads.Select(ad =>
            {
                var scopedLeads = leads.Where(l => l.AdId == ad.AdId).ToList();
                var total = scopedLeads.Count;
                var newCount = scopedLeads.Count(l => l.Status == "new");
                var contacted = scopedLeads.Count(l => l.Status is "contacted" or "responded" or "qualified" or "booked");
                var qualified = scopedLeads.Count(l => l.Status is "qualified" or "booked");
                var booked = scopedLeads.Count(l => l.Status == "booked");
                return new
                {
                    adId = ad.AdId,
                    adName = ad.Name,
                    campaignId = ad.CampaignId,
                    adsetId = ad.AdsetId,
                    status = ad.Status,
                    platform = ad.Platform,
                    leadForm = ad.FormId,
                    leads = total,
                    newLeads = newCount,
                    contactedRate = total == 0 ? 0m : Math.Round(contacted * 100m / total, 2),
                    qualifiedRate = total == 0 ? 0m : Math.Round(qualified * 100m / total, 2),
                    bookedRate = total == 0 ? 0m : Math.Round(booked * 100m / total, 2),
                    lastLeadAtUtc = scopedLeads.MaxBy(x => x.CreatedTime)?.CreatedTime ?? ad.LastLeadAtUtc,
                    owner = scopedLeads.Where(x => !string.IsNullOrWhiteSpace(x.AssignedUserId)).GroupBy(x => x.AssignedUserId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault()
                };
            }).ToList();

            var p = page.GetValueOrDefault(1);
            var ps = Math.Clamp(pageSize.GetValueOrDefault(25), 10, 200);
            var totalRows = rows.Count;
            var paged = rows.Skip((p - 1) * ps).Take(ps).ToList();
            return Results.Ok(new { success = true, page = p, pageSize = ps, total = totalRows, rows = paged });
        }).RequireAuthorization();

        app.MapGet("/api/social/facebook/ads/stats", async (
            Guid integrationConnectionId,
            DateTimeOffset? dateFrom,
            DateTimeOffset? dateTo,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            await SocialMarketingSchemaService.EnsureSchemaAsync(db);
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null)
            {
                return Results.Unauthorized();
            }
            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);

            var from = dateFrom ?? DateTimeOffset.UtcNow.AddDays(-30);
            var to = dateTo ?? DateTimeOffset.UtcNow;
            var leads = await db.MktMetaLeads.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId && x.CreatedTime >= from && x.CreatedTime <= to)
                .ToListAsync(cancellationToken);

            var total = leads.Count;
            var newCount = leads.Count(x => x.Status == "new");
            var contacted = leads.Count(x => x.Status is "contacted" or "responded" or "qualified" or "booked");
            var qualified = leads.Count(x => x.Status is "qualified" or "booked");
            var booked = leads.Count(x => x.Status == "booked");
            var avgResponseMinutes = leads
                .Where(x => x.LastActionAtUtc.HasValue)
                .Select(x => Math.Max(0, (x.LastActionAtUtc!.Value - x.CreatedTime.UtcDateTime).TotalMinutes))
                .DefaultIfEmpty(0)
                .Average();

            return Results.Ok(new
            {
                success = true,
                totalLeads = total,
                newLeads = newCount,
                contactedRate = total == 0 ? 0m : Math.Round(contacted * 100m / total, 2),
                qualifiedRate = total == 0 ? 0m : Math.Round(qualified * 100m / total, 2),
                bookedRate = total == 0 ? 0m : Math.Round(booked * 100m / total, 2),
                avgResponseMinutes = Math.Round(avgResponseMinutes, 2)
            });
        }).RequireAuthorization();

        app.MapGet("/api/social/reports/overview", async (
            Guid integrationConnectionId,
            DateTimeOffset? dateFrom,
            DateTimeOffset? dateTo,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            SocialFacebookMarketingService socialService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            var connection = await db.IntegrationConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == integrationConnectionId && x.OwnerUserId == user.Id && x.Provider == "facebook", cancellationToken);
            if (connection == null)
            {
                return Results.NotFound(new { success = false, message = "Facebook integration connection not found." });
            }

            var result = await socialService.GetReportingSnapshotAsync(user, integrationConnectionId, dateFrom, dateTo, cancellationToken);
            return Results.Ok(result);
        }).RequireAuthorization();

        app.MapGet("/api/social/facebook/ads/{adId}/leads", async (
            string adId,
            Guid integrationConnectionId,
            string? status,
            string? owner,
            string? search,
            bool? onlyDuplicates,
            DateTimeOffset? dateFrom,
            DateTimeOffset? dateTo,
            int? page,
            int? pageSize,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            await SocialMarketingSchemaService.EnsureSchemaAsync(db);
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null)
            {
                return Results.Unauthorized();
            }

            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
            var from = dateFrom ?? DateTimeOffset.UtcNow.AddDays(-30);
            var to = dateTo ?? DateTimeOffset.UtcNow;

            var query = db.MktMetaLeads.AsNoTracking().Where(x =>
                x.TenantId == tenantId &&
                x.WorkspaceId == workspaceId &&
                x.IntegrationConnectionId == integrationConnectionId &&
                x.AdId == adId &&
                x.CreatedTime >= from &&
                x.CreatedTime <= to);

            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(owner)) query = query.Where(x => x.AssignedUserId == owner.Trim());
            if (onlyDuplicates == true) query = query.Where(x => x.DuplicateOfLeadId != null);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLowerInvariant();
                query = query.Where(x =>
                    (x.NormalizedName ?? string.Empty).ToLower().Contains(s) ||
                    (x.NormalizedEmail ?? string.Empty).ToLower().Contains(s) ||
                    (x.NormalizedPhone ?? string.Empty).ToLower().Contains(s));
            }

            var p = page.GetValueOrDefault(1);
            var ps = Math.Clamp(pageSize.GetValueOrDefault(30), 10, 200);
            var total = await query.CountAsync(cancellationToken);
            var leads = await query.OrderByDescending(x => x.CreatedTime).Skip((p - 1) * ps).Take(ps).ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                success = true,
                page = p,
                pageSize = ps,
                total,
                rows = leads.Select(l => new
                {
                    leadPkId = l.Id,
                    leadId = l.LeadId,
                    name = l.NormalizedName,
                    email = l.NormalizedEmail,
                    phone = l.NormalizedPhone,
                    city = l.City,
                    country = l.Country,
                    submittedAt = l.CreatedTime,
                    status = l.Status,
                    assignedTo = l.AssignedUserId,
                    lastActionAt = l.LastActionAtUtc,
                    score = l.Score,
                    duplicateOfLeadId = l.DuplicateOfLeadId
                })
            });
        }).RequireAuthorization();

        app.MapGet("/api/social/facebook/leads/{leadPkId:guid}", async (
            Guid leadPkId,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            SocialFacebookMarketingService socialService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            await SocialMarketingSchemaService.EnsureSchemaAsync(db);
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null)
            {
                return Results.Unauthorized();
            }
            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);

            var lead = await db.MktMetaLeads.AsNoTracking().FirstOrDefaultAsync(x => x.Id == leadPkId && x.TenantId == tenantId && x.WorkspaceId == workspaceId, cancellationToken);
            if (lead == null)
            {
                return Results.NotFound(new { success = false, message = "Lead not found." });
            }

            var context = await socialService.BuildLeadContextAsync(lead, user, "ui", "lead_opened", cancellationToken);
            return Results.Ok(new { success = true, lead, leadContext = context });
        }).RequireAuthorization();

        app.MapGet("/api/social/facebook/leads/{leadPkId:guid}/activities", async (
            Guid leadPkId,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            await SocialMarketingSchemaService.EnsureSchemaAsync(db);
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null)
            {
                return Results.Unauthorized();
            }
            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);

            var leadExists = await db.MktMetaLeads.AsNoTracking().AnyAsync(x => x.Id == leadPkId && x.TenantId == tenantId && x.WorkspaceId == workspaceId, cancellationToken);
            if (!leadExists)
            {
                return Results.NotFound(new { success = false, message = "Lead not found." });
            }

            var activities = await db.MktLeadActivities.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.LeadPkId == leadPkId)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(200)
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                success = true,
                activities = activities.Select(a => new
                {
                    id = a.Id,
                    type = a.Type,
                    payload = SafeParseJsonObject(a.PayloadJson),
                    createdBy = a.CreatedBy,
                    createdAt = a.CreatedAtUtc
                })
            });
        }).RequireAuthorization();

        app.MapPost("/api/social/facebook/leads/{leadPkId:guid}/assign", async (
            Guid leadPkId,
            LeadAssignRequest request,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            SocialFacebookMarketingService socialService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var lead = await FindOwnedLeadAsync(leadPkId, db, userManager, httpContext.User, cancellationToken);
            if (lead == null) return Results.NotFound(new { success = false, message = "Lead not found." });

            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();
            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);

            lead.AssignedUserId = string.IsNullOrWhiteSpace(request.AssignedUserId) ? null : request.AssignedUserId.Trim();
            lead.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            var result = await socialService.AddLeadActivityAsync(lead, tenantId, workspaceId, "assigned", new { assignedUserId = lead.AssignedUserId }, user.Id, cancellationToken);
            return Results.Ok(result);
        }).RequireAuthorization().DisableAntiforgery();

        app.MapPost("/api/social/facebook/leads/{leadPkId:guid}/status", async (
            Guid leadPkId,
            LeadStatusRequest request,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            SocialFacebookMarketingService socialService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var lead = await FindOwnedLeadAsync(leadPkId, db, userManager, httpContext.User, cancellationToken);
            if (lead == null) return Results.NotFound(new { success = false, message = "Lead not found." });

            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();
            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);

            var nextStatus = string.IsNullOrWhiteSpace(request.Status) ? "new" : request.Status.Trim().ToLowerInvariant();
            lead.Status = nextStatus;
            lead.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            var result = await socialService.AddLeadActivityAsync(lead, tenantId, workspaceId, "status_changed", new { status = nextStatus }, user.Id, cancellationToken);
            return Results.Ok(result);
        }).RequireAuthorization().DisableAntiforgery();

        app.MapPost("/api/social/facebook/leads/{leadPkId:guid}/note", async (
            Guid leadPkId,
            LeadNoteRequest request,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            SocialFacebookMarketingService socialService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var lead = await FindOwnedLeadAsync(leadPkId, db, userManager, httpContext.User, cancellationToken);
            if (lead == null) return Results.NotFound(new { success = false, message = "Lead not found." });
            if (string.IsNullOrWhiteSpace(request.Text)) return Results.BadRequest(new { success = false, message = "text is required." });

            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();
            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
            var result = await socialService.AddLeadActivityAsync(lead, tenantId, workspaceId, "note_added", new { text = request.Text.Trim() }, user.Id, cancellationToken);
            return Results.Ok(result);
        }).RequireAuthorization().DisableAntiforgery();

        app.MapPost("/api/social/facebook/leads/{leadPkId:guid}/send/whatsapp", async (
            Guid leadPkId,
            LeadMessageRequest request,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IWhatsAppSender whatsAppSender,
            SocialFacebookMarketingService socialService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var lead = await FindOwnedLeadAsync(leadPkId, db, userManager, httpContext.User, cancellationToken);
            if (lead == null) return Results.NotFound(new { success = false, message = "Lead not found." });
            if (string.IsNullOrWhiteSpace(lead.NormalizedPhone)) return Results.BadRequest(new { success = false, message = "Lead does not have a valid phone number." });
            if (string.IsNullOrWhiteSpace(request.MessageText)) return Results.BadRequest(new { success = false, message = "messageText is required." });

            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();
            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
            var send = await whatsAppSender.SendAsync(lead.NormalizedPhone, request.MessageText.Trim(), cancellationToken);
            var activityType = send.Success ? "whatsapp_sent" : "whatsapp_failed";
            var result = await socialService.AddLeadActivityAsync(lead, tenantId, workspaceId, activityType, new
            {
                to = lead.NormalizedPhone,
                templateId = request.TemplateId,
                messageText = request.MessageText,
                variables = request.Variables ?? new Dictionary<string, string?>(),
                error = send.Error
            }, user.Id, cancellationToken);
            result.ProviderMessageId = send.ProviderMessageId;
            return send.Success ? Results.Ok(result) : Results.BadRequest(new { success = false, message = send.Error, activity = result });
        }).RequireAuthorization().DisableAntiforgery();

        app.MapPost("/api/social/facebook/leads/{leadPkId:guid}/send/email", async (
            Guid leadPkId,
            LeadEmailRequest request,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender,
            SocialFacebookMarketingService socialService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var lead = await FindOwnedLeadAsync(leadPkId, db, userManager, httpContext.User, cancellationToken);
            if (lead == null) return Results.NotFound(new { success = false, message = "Lead not found." });
            if (string.IsNullOrWhiteSpace(request.Subject)) return Results.BadRequest(new { success = false, message = "subject is required." });
            if (string.IsNullOrWhiteSpace(lead.NormalizedEmail)) return Results.BadRequest(new { success = false, message = "Lead does not have a valid email address." });

            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();
            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
            var send = await emailSender.SendAsync(lead.NormalizedEmail, request.Subject.Trim(), request.HtmlBody ?? string.Empty);
            var activityType = send.Success ? "email_sent" : "email_failed";
            var result = await socialService.AddLeadActivityAsync(lead, tenantId, workspaceId, activityType, new
            {
                to = lead.NormalizedEmail,
                subject = request.Subject.Trim(),
                htmlBody = request.HtmlBody ?? string.Empty,
                variables = request.Variables ?? new Dictionary<string, string?>(),
                error = send.Error
            }, user.Id, cancellationToken);
            result.ProviderMessageId = send.Success ? $"mail_{Guid.NewGuid():N}" : null;
            return send.Success ? Results.Ok(result) : Results.BadRequest(new { success = false, message = send.Error, activity = result });
        }).RequireAuthorization().DisableAntiforgery();

        app.MapPost("/api/social/facebook/leads/{leadPkId:guid}/task", async (
            Guid leadPkId,
            LeadTaskRequest request,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            SocialFacebookMarketingService socialService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var lead = await FindOwnedLeadAsync(leadPkId, db, userManager, httpContext.User, cancellationToken);
            if (lead == null) return Results.NotFound(new { success = false, message = "Lead not found." });
            if (string.IsNullOrWhiteSpace(request.Title)) return Results.BadRequest(new { success = false, message = "title is required." });

            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();
            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
            var result = await socialService.AddLeadActivityAsync(lead, tenantId, workspaceId, "task_created", new
            {
                title = request.Title.Trim(),
                dueAt = request.DueAt,
                priority = request.Priority,
                assignedUserId = request.AssignedUserId
            }, user.Id, cancellationToken);
            return Results.Ok(result);
        }).RequireAuthorization().DisableAntiforgery();

        app.MapPost("/api/social/facebook/leads/{leadPkId:guid}/push-to-crm", async (
            Guid leadPkId,
            LeadPushToCrmRequest request,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            ICrmPushService crmPushService,
            SocialFacebookMarketingService socialService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var lead = await FindOwnedLeadAsync(leadPkId, db, userManager, httpContext.User, cancellationToken);
            if (lead == null) return Results.NotFound(new { success = false, message = "Lead not found." });

            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();
            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
            var mode = string.IsNullOrWhiteSpace(request.Mode) ? "lead" : request.Mode.Trim().ToLowerInvariant();
            var leadContext = await socialService.BuildLeadContextAsync(lead, user, "manual", "crm_push", cancellationToken);
            var push = await crmPushService.PushAsync(new
            {
                mode,
                pipelineId = request.PipelineId,
                stageId = request.StageId,
                leadContext
            }, mode, cancellationToken);
            var activityType = push.Success ? "crm_push" : "crm_push_failed";
            var result = await socialService.AddLeadActivityAsync(lead, tenantId, workspaceId, activityType, new
            {
                mode,
                pipelineId = request.PipelineId,
                stageId = request.StageId,
                crmIds = push.CrmIds,
                error = push.Error
            }, user.Id, cancellationToken);
            return push.Success
                ? Results.Ok(new { result.Ok, result.Action, result.LeadId, result.LoggedActivityId, result.Timestamp, crm = push.CrmIds })
                : Results.BadRequest(new { success = false, message = push.Error, activity = result, crm = push.CrmIds });
        }).RequireAuthorization().DisableAntiforgery();

        app.MapPost("/api/social/facebook/leads/{leadPkId:guid}/run-workflow", async (
            Guid leadPkId,
            LeadRunWorkflowRequest request,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            SocialFacebookMarketingService socialService,
            WorkflowExecutionService workflowExecutionService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (request.WorkflowId == Guid.Empty) return Results.BadRequest(new { success = false, message = "workflowId is required." });

            var lead = await FindOwnedLeadAsync(leadPkId, db, userManager, httpContext.User, cancellationToken);
            if (lead == null) return Results.NotFound(new { success = false, message = "Lead not found." });

            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();
            var workflow = await db.Workflows.FirstOrDefaultAsync(x => x.Id == request.WorkflowId && x.OwnerUserId == user.Id, cancellationToken);
            if (workflow == null) return Results.NotFound(new { success = false, message = "Workflow not found." });

            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
            var leadContext = await socialService.BuildLeadContextAsync(lead, user, "manual", "manual_run", cancellationToken);
            var fields = leadContext.Lead.Answers.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            var triggerContext = new WorkflowTriggerContext(
                Email: lead.NormalizedEmail,
                Fields: fields,
                SourceUrl: null,
                ElementTag: "socialMarketing",
                ElementId: lead.Id.ToString(),
                Provider: "facebook",
                BranchKey: "primary",
                RawPayloadJson: JsonSerializer.Serialize(leadContext),
                MappedFields: fields,
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
                TriggerType = "manual_run",
                SourceEntity = "mkt_lead",
                SourceId = lead.Id,
                Status = "running",
                LogsJson = "{}",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.MktWorkflowRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken);

            var (ok, error) = await workflowExecutionService.ExecuteAsync(workflow, triggerContext);
            run.Status = ok ? "success" : "fail";
            run.LogsJson = JsonSerializer.Serialize(new { ok, error });
            run.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            await socialService.AddLeadActivityAsync(lead, tenantId, workspaceId, "workflow_run", new
            {
                workflowId = workflow.Id,
                workflowName = workflow.Caption ?? workflow.Name,
                workflowRunId = run.Id,
                ok,
                error
            }, user.Id, cancellationToken);

            return Results.Ok(new { success = ok, workflowRunId = run.Id, error });
        }).RequireAuthorization().DisableAntiforgery();

        app.MapGet("/api/workflows/runs/{workflowRunId:guid}", async (
            Guid workflowRunId,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();
            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
            var run = await db.MktWorkflowRuns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == workflowRunId && x.TenantId == tenantId && x.WorkspaceId == workspaceId, cancellationToken);
            if (run == null) return Results.NotFound(new { success = false, message = "Workflow run not found." });

            return Results.Ok(new
            {
                success = true,
                workflowRunId = run.Id,
                run.WorkflowId,
                run.TriggerType,
                run.SourceEntity,
                run.SourceId,
                run.Status,
                logs = SafeParseJsonObject(run.LogsJson),
                run.CreatedAtUtc,
                run.UpdatedAtUtc
            });
        }).RequireAuthorization();

        app.MapGet("/api/social/facebook/settings", async (
            Guid integrationConnectionId,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            await SocialMarketingSchemaService.EnsureSchemaAsync(db);
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();

            var connection = await db.IntegrationConnections.FirstOrDefaultAsync(x => x.Id == integrationConnectionId && x.OwnerUserId == user.Id && x.Provider == "facebook", cancellationToken);
            if (connection == null) return Results.NotFound(new { success = false, message = "Facebook integration connection not found." });

            var settings = ParseConnectionSettings(connection.MetadataJson);
            settings.IntegrationConnectionId = connection.Id;
            return Results.Ok(new { success = true, settings });
        }).RequireAuthorization();

        app.MapPut("/api/social/facebook/settings", async (
            SocialFacebookSettingsRequest request,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            await SocialMarketingSchemaService.EnsureSchemaAsync(db);
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();
            if (request.IntegrationConnectionId == Guid.Empty) return Results.BadRequest(new { success = false, message = "integrationConnectionId is required." });

            var connection = await db.IntegrationConnections.FirstOrDefaultAsync(x => x.Id == request.IntegrationConnectionId && x.OwnerUserId == user.Id && x.Provider == "facebook", cancellationToken);
            if (connection == null) return Results.NotFound(new { success = false, message = "Facebook integration connection not found." });

            var settings = new SocialFacebookSettingsDto
            {
                IntegrationConnectionId = connection.Id,
                WebhookEnabled = request.WebhookEnabled,
                PollingIntervalMinutes = request.PollingIntervalMinutes <= 0 ? 10 : request.PollingIntervalMinutes,
                DataRetentionDays = request.DataRetentionDays <= 0 ? 180 : request.DataRetentionDays,
                ResponseSlaMinutes = request.ResponseSlaMinutes <= 0 ? 10 : request.ResponseSlaMinutes,
                RequireConsent = request.RequireConsent,
                WorkingHoursStart = string.IsNullOrWhiteSpace(request.WorkingHoursStart) ? "09:00" : request.WorkingHoursStart,
                WorkingHoursEnd = string.IsNullOrWhiteSpace(request.WorkingHoursEnd) ? "18:00" : request.WorkingHoursEnd,
                Templates = request.Templates ?? []
            };
            connection.MetadataJson = MergeConnectionSettings(connection.MetadataJson, settings);
            connection.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { success = true, settings });
        }).RequireAuthorization().DisableAntiforgery();

        app.MapGet("/api/social/facebook/ops/health", async (
            Guid integrationConnectionId,
            UserManager<ApplicationUser> userManager,
            SocialFacebookMarketingService socialService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();
            var result = await socialService.GetOpsSnapshotAsync(user, integrationConnectionId, cancellationToken);
            return Results.Ok(result);
        }).RequireAuthorization();

        app.MapGet("/api/social/facebook/sync/logs", async (
            Guid integrationConnectionId,
            UserManager<ApplicationUser> userManager,
            SocialFacebookMarketingService socialService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();
            var rows = await socialService.GetSyncLogsAsync(user, integrationConnectionId, cancellationToken);
            return Results.Ok(new
            {
                success = true,
                rows = rows.Select(x => new
                {
                    id = x.Id,
                    scope = x.Scope,
                    status = x.Status,
                    startedAtUtc = x.StartedAtUtc,
                    endedAtUtc = x.EndedAtUtc,
                    counts = SafeParseJsonObject(x.CountsJson),
                    error = SafeParseJsonObject(x.ErrorJson)
                })
            });
        }).RequireAuthorization();

        app.MapGet("/api/social/facebook/dead-letter", async (
            Guid integrationConnectionId,
            UserManager<ApplicationUser> userManager,
            SocialFacebookMarketingService socialService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();
            var rows = await socialService.GetDeadLettersAsync(user, integrationConnectionId, cancellationToken);
            return Results.Ok(new
            {
                success = true,
                rows = rows.Select(x => new
                {
                    id = x.Id,
                    eventType = x.EventType,
                    attempts = x.Attempts,
                    createdAtUtc = x.CreatedAtUtc,
                    resolvedAtUtc = x.ResolvedAtUtc,
                    error = SafeParseJsonObject(x.ErrorJson)
                })
            });
        }).RequireAuthorization();

        app.MapPost("/api/social/facebook/dead-letter/{id:guid}/retry", async (
            Guid id,
            UserManager<ApplicationUser> userManager,
            SocialFacebookMarketingService socialService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();
            var result = await socialService.RetryDeadLetterAsync(user, id, cancellationToken);
            return Results.Ok(result);
        }).RequireAuthorization().DisableAntiforgery();

        app.MapGet("/api/social/facebook/auto-rules", async (
            Guid integrationConnectionId,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            await SocialMarketingSchemaService.EnsureSchemaAsync(db);
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();

            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
            var rows = await db.MktAutoRules.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.WorkspaceId == workspaceId && x.IntegrationConnectionId == integrationConnectionId && x.Channel == "facebook")
                .OrderBy(x => x.EventType)
                .ToListAsync(cancellationToken);
            return Results.Ok(new
            {
                success = true,
                rows = rows.Select(x => new
                {
                    id = x.Id,
                    channel = x.Channel,
                    eventType = x.EventType,
                    workflowId = x.WorkflowId,
                    isEnabled = x.IsEnabled,
                    conditions = SafeParseJsonObject(x.ConditionsJson),
                    createdAtUtc = x.CreatedAtUtc,
                    updatedAtUtc = x.UpdatedAtUtc
                })
            });
        }).RequireAuthorization();

        app.MapPost("/api/social/facebook/auto-rules", async (
            SocialAutoRuleRequest request,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            await SocialMarketingSchemaService.EnsureSchemaAsync(db);
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();
            if (request.IntegrationConnectionId == Guid.Empty || request.WorkflowId == Guid.Empty || string.IsNullOrWhiteSpace(request.EventType))
            {
                return Results.BadRequest(new { success = false, message = "integrationConnectionId, eventType, and workflowId are required." });
            }

            var workflow = await db.Workflows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.WorkflowId && x.OwnerUserId == user.Id, cancellationToken);
            if (workflow == null) return Results.NotFound(new { success = false, message = "Workflow not found." });

            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
            var row = new MktAutoRule
            {
                TenantId = tenantId,
                WorkspaceId = workspaceId,
                IntegrationConnectionId = request.IntegrationConnectionId,
                Channel = "facebook",
                EventType = request.EventType.Trim().ToLowerInvariant(),
                WorkflowId = request.WorkflowId,
                IsEnabled = request.IsEnabled,
                ConditionsJson = request.ConditionsJson ?? "{}",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.MktAutoRules.Add(row);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { success = true, id = row.Id });
        }).RequireAuthorization().DisableAntiforgery();

        app.MapPut("/api/social/facebook/auto-rules/{id:guid}", async (
            Guid id,
            SocialAutoRuleRequest request,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            await SocialMarketingSchemaService.EnsureSchemaAsync(db);
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();

            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
            var row = await db.MktAutoRules.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.WorkspaceId == workspaceId, cancellationToken);
            if (row == null) return Results.NotFound(new { success = false, message = "Rule not found." });

            if (request.WorkflowId != Guid.Empty)
            {
                var workflow = await db.Workflows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.WorkflowId && x.OwnerUserId == user.Id, cancellationToken);
                if (workflow == null) return Results.NotFound(new { success = false, message = "Workflow not found." });
                row.WorkflowId = request.WorkflowId;
            }
            if (!string.IsNullOrWhiteSpace(request.EventType)) row.EventType = request.EventType.Trim().ToLowerInvariant();
            row.IsEnabled = request.IsEnabled;
            row.ConditionsJson = request.ConditionsJson ?? row.ConditionsJson;
            row.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { success = true });
        }).RequireAuthorization().DisableAntiforgery();

        app.MapDelete("/api/social/facebook/auto-rules/{id:guid}", async (
            Guid id,
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            await SocialMarketingSchemaService.EnsureSchemaAsync(db);
            var user = await userManager.GetUserAsync(httpContext.User);
            if (user == null) return Results.Unauthorized();

            var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
            var row = await db.MktAutoRules.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && x.WorkspaceId == workspaceId, cancellationToken);
            if (row == null) return Results.NotFound(new { success = false, message = "Rule not found." });

            db.MktAutoRules.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { success = true });
        }).RequireAuthorization().DisableAntiforgery();

        app.MapPost("/api/webhooks/meta/lead", async (
            HttpContext httpContext,
            IOptions<FeatureFlagOptions> featureFlags,
            ILoggerFactory loggerFactory,
            SocialFacebookMarketingService socialService,
            CancellationToken cancellationToken) =>
        {
            if (!featureFlags.Value.MetaWebhookLive)
            {
                return Results.Ok(new { success = true, mode = "disabled", accepted = true });
            }

            using var reader = new StreamReader(httpContext.Request.Body);
            var raw = await reader.ReadToEndAsync(cancellationToken);
            var logger = loggerFactory.CreateLogger("MetaLeadWebhook");
            if (!IsMetaSignatureValid(httpContext.Request.Headers["X-Hub-Signature-256"], raw))
            {
                logger.LogWarning("Meta webhook rejected due to invalid signature.");
                return Results.Unauthorized();
            }

            using var doc = JsonDocument.Parse(raw);
            var result = await socialService.IngestWebhookAsync(doc.RootElement, cancellationToken);
            return Results.Ok(result);
        }).DisableAntiforgery();

        app.MapMethods("/api/webhooks/meta/lead", ["GET"], (HttpContext httpContext) =>
        {
            var mode = httpContext.Request.Query["hub.mode"].ToString();
            var token = httpContext.Request.Query["hub.verify_token"].ToString();
            var challenge = httpContext.Request.Query["hub.challenge"].ToString();
            var expectedToken = Environment.GetEnvironmentVariable("BUGENCE_META_VERIFY_TOKEN");

            if (string.Equals(mode, "subscribe", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(expectedToken) &&
                string.Equals(token, expectedToken, StringComparison.Ordinal))
            {
                return Results.Text(challenge, "text/plain");
            }

            return Results.Unauthorized();
        });

        return app;
    }

    private static async Task<MktMetaLead?> FindOwnedLeadAsync(
        Guid leadPkId,
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        await SocialMarketingSchemaService.EnsureSchemaAsync(db);
        var user = await userManager.GetUserAsync(principal);
        if (user == null) return null;
        var (tenantId, workspaceId) = SocialFacebookMarketingService.ResolveTenantWorkspace(user);
        return await db.MktMetaLeads.FirstOrDefaultAsync(x => x.Id == leadPkId && x.TenantId == tenantId && x.WorkspaceId == workspaceId, cancellationToken);
    }

    private static object SafeParseJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new { };
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch
        {
            return new { raw };
        }
    }

    private static SocialFacebookSettingsDto ParseConnectionSettings(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return new SocialFacebookSettingsDto();
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (!doc.RootElement.TryGetProperty("socialFacebookSettings", out var settingsNode))
            {
                return new SocialFacebookSettingsDto();
            }
            return JsonSerializer.Deserialize<SocialFacebookSettingsDto>(settingsNode.GetRawText()) ?? new SocialFacebookSettingsDto();
        }
        catch
        {
            return new SocialFacebookSettingsDto();
        }
    }

    private static string MergeConnectionSettings(string? metadataJson, SocialFacebookSettingsDto settings)
    {
        var root = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            try
            {
                root = JsonSerializer.Deserialize<Dictionary<string, object?>>(metadataJson) ?? root;
            }
            catch
            {
                root = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            }
        }
        root["socialFacebookSettings"] = settings;
        return JsonSerializer.Serialize(root);
    }

    private static bool IsMetaSignatureValid(string? signatureHeader, string rawBody)
    {
        var appSecret = Environment.GetEnvironmentVariable("BUGENCE_META_APP_SECRET");
        if (string.IsNullOrWhiteSpace(appSecret))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        var computed = Convert.ToHexString(hash).ToLowerInvariant();
        var provided = signatureHeader["sha256=".Length..].Trim().ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(provided));
    }
}

public sealed class SocialSyncAdsRequest
{
    public Guid IntegrationConnectionId { get; set; }
}

public sealed class SocialSyncLeadsRequest
{
    public Guid IntegrationConnectionId { get; set; }
    public int LookbackMinutes { get; set; } = 15;
}

public sealed class LeadAssignRequest
{
    public string? AssignedUserId { get; set; }
}

public sealed class LeadStatusRequest
{
    public string Status { get; set; } = "new";
}

public sealed class LeadNoteRequest
{
    public string Text { get; set; } = string.Empty;
}

public sealed class LeadMessageRequest
{
    public string? TemplateId { get; set; }
    public string? MessageText { get; set; }
    public Dictionary<string, string?>? Variables { get; set; }
}

public sealed class LeadEmailRequest
{
    public string Subject { get; set; } = string.Empty;
    public string? HtmlBody { get; set; }
    public Dictionary<string, string?>? Variables { get; set; }
}

public sealed class LeadTaskRequest
{
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset? DueAt { get; set; }
    public string Priority { get; set; } = "normal";
    public string? AssignedUserId { get; set; }
}

public sealed class LeadPushToCrmRequest
{
    public string Mode { get; set; } = "lead";
    public string? PipelineId { get; set; }
    public string? StageId { get; set; }
}

public sealed class LeadRunWorkflowRequest
{
    public Guid WorkflowId { get; set; }
}

public sealed class SocialFacebookSettingsRequest
{
    public Guid IntegrationConnectionId { get; set; }
    public bool WebhookEnabled { get; set; } = true;
    public int PollingIntervalMinutes { get; set; } = 10;
    public int DataRetentionDays { get; set; } = 180;
    public int ResponseSlaMinutes { get; set; } = 10;
    public bool RequireConsent { get; set; }
    public string? WorkingHoursStart { get; set; } = "09:00";
    public string? WorkingHoursEnd { get; set; } = "18:00";
    public List<MarketingMessageTemplateDto>? Templates { get; set; }
}

public sealed class SocialFacebookSettingsDto
{
    public Guid IntegrationConnectionId { get; set; }
    public bool WebhookEnabled { get; set; } = true;
    public int PollingIntervalMinutes { get; set; } = 10;
    public int DataRetentionDays { get; set; } = 180;
    public int ResponseSlaMinutes { get; set; } = 10;
    public bool RequireConsent { get; set; }
    public string WorkingHoursStart { get; set; } = "09:00";
    public string WorkingHoursEnd { get; set; } = "18:00";
    public List<MarketingMessageTemplateDto> Templates { get; set; } = [];
}

public sealed class MarketingMessageTemplateDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Channel { get; set; } = "whatsapp";
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public sealed class SocialAutoRuleRequest
{
    public Guid IntegrationConnectionId { get; set; }
    public string EventType { get; set; } = "lead_received";
    public Guid WorkflowId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? ConditionsJson { get; set; }
}
