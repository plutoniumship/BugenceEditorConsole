using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Settings;

[Authorize]
public class GlobalSettingsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISubscriptionService _subscriptionService;

    public GlobalSettingsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ISubscriptionService subscriptionService)
    {
        _db = db;
        _userManager = userManager;
        _subscriptionService = subscriptionService;
    }

    public WorkspaceSnapshot Workspace { get; private set; } = new();
    public List<ProjectOption> Projects { get; private set; } = new();
    public ProjectOption? SelectedProject { get; private set; }
    public BillingSnapshot Billing { get; private set; } = new();
    public ApiAccessSnapshot ApiAccess { get; private set; } = new();
    public WebhooksSnapshot Webhooks { get; private set; } = new();
    public NotificationsSnapshot Notifications { get; private set; } = new();
    public SecuritySnapshot Security { get; private set; } = new();
    public IntegrationsSnapshot Integrations { get; private set; } = new();
    public AuditSnapshot Audit { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int? projectId = null)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return RedirectToPage("/Auth/Login", new { returnUrl = Url.Page("/Settings/GlobalSettings") });
        }

        var projectScope = BuildProjectScope(context);
        var projects = await projectScope
            .OrderByDescending(p => p.LastPublishedAtUtc ?? p.UploadedAtUtc)
            .ThenByDescending(p => p.UploadedAtUtc)
            .Select(p => new ProjectRecord
            {
                Id = p.Id,
                FolderName = p.FolderName,
                DisplayName = p.DisplayName,
                Slug = p.Slug,
                Status = p.Status,
                SizeBytes = p.SizeBytes,
                UploadedAtUtc = p.UploadedAtUtc,
                LastPublishedAtUtc = p.LastPublishedAtUtc
            })
            .ToListAsync();

        var projectIds = projects.Select(x => x.Id).ToList();
        var fileCounts = projectIds.Count == 0
            ? new Dictionary<int, int>()
            : await _db.UploadedProjectFiles.AsNoTracking()
                .Where(f => projectIds.Contains(f.UploadedProjectId))
                .GroupBy(f => f.UploadedProjectId)
                .Select(g => new { ProjectId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ProjectId, x => x.Count);
        var domainCounts = projectIds.Count == 0
            ? new Dictionary<int, int>()
            : await _db.ProjectDomains.AsNoTracking()
                .Where(d => projectIds.Contains(d.UploadedProjectId))
                .GroupBy(d => d.UploadedProjectId)
                .Select(g => new { ProjectId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ProjectId, x => x.Count);
        var latestPreflights = projectIds.Count == 0
            ? new Dictionary<int, ProjectPreflightRun>()
            : await _db.ProjectPreflightRuns.AsNoTracking()
                .Where(r => projectIds.Contains(r.UploadedProjectId))
                .GroupBy(r => r.UploadedProjectId)
                .Select(g => g.OrderByDescending(x => x.CreatedAtUtc).First())
                .ToDictionaryAsync(x => x.UploadedProjectId);
        var environmentPointers = projectIds.Count == 0
            ? new Dictionary<int, ProjectEnvironmentPointer>()
            : await _db.ProjectEnvironmentPointers.AsNoTracking()
                .Where(p => projectIds.Contains(p.UploadedProjectId))
                .ToDictionaryAsync(x => x.UploadedProjectId);

        Projects = projects.Select(project =>
        {
            latestPreflights.TryGetValue(project.Id, out var preflight);
            environmentPointers.TryGetValue(project.Id, out var pointer);
            fileCounts.TryGetValue(project.Id, out var fileCount);
            domainCounts.TryGetValue(project.Id, out var domainCount);
            return new ProjectOption
            {
                Id = project.Id,
                Name = string.IsNullOrWhiteSpace(project.DisplayName) ? project.FolderName : project.DisplayName!,
                Slug = project.Slug,
                Status = string.IsNullOrWhiteSpace(project.Status) ? "Uploaded" : project.Status,
                SizeBytes = project.SizeBytes,
                SizeLabel = FormatBytes(project.SizeBytes),
                UploadedAtUtc = project.UploadedAtUtc,
                LastPublishedAtUtc = project.LastPublishedAtUtc,
                FileCount = fileCount,
                DomainCount = domainCount,
                PreflightScore = preflight?.Score,
                PreflightSafe = preflight?.Safe,
                EnvironmentLabel = ResolveEnvironment(pointer, project.LastPublishedAtUtc),
                ActivityLabel = project.LastPublishedAtUtc.HasValue ? $"Published {FormatRelative(project.LastPublishedAtUtc.Value)}" : $"Uploaded {FormatRelative(project.UploadedAtUtc)}"
            };
        }).ToList();

        SelectedProject = Projects.FirstOrDefault(p => p.Id == projectId) ?? Projects.FirstOrDefault();

        var connectedIntegrations = await _db.IntegrationConnections.AsNoTracking()
            .Where(x => x.OwnerUserId == context.OwnerUserId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync();
        var teamMembers = await _db.TeamMembers.AsNoTracking()
            .Where(m => m.OwnerUserId == context.OwnerUserId)
            .OrderBy(m => m.DisplayName)
            .ToListAsync();
        var pendingInvites = await _db.TeamInvites.AsNoTracking()
            .Where(i => i.OwnerUserId == context.OwnerUserId && i.ConsumedAtUtc == null && i.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(i => i.CreatedAtUtc)
            .ToListAsync();
        var notificationItems = await _db.UserNotifications.AsNoTracking()
            .Where(n => n.UserId == context.User.Id)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(12)
            .ToListAsync();
        var notificationTypeCounts = notificationItems
            .GroupBy(n => (n.Type ?? "info").Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var subscription = await _subscriptionService.GetOrStartTrialAsync(context.OwnerUserId);
        var paymentMethods = await _db.UserPaymentMethods.AsNoTracking()
            .Where(p => p.UserId == context.OwnerUserId)
            .OrderByDescending(p => p.IsDefault)
            .ThenByDescending(p => p.CreatedAtUtc)
            .Take(4)
            .ToListAsync();

        var workflows = await _db.Workflows.AsNoTracking()
            .Where(w => w.OwnerUserId == context.OwnerUserId && (!context.CompanyId.HasValue || w.CompanyId == context.CompanyId))
            .OrderByDescending(w => w.UpdatedAtUtc)
            .ToListAsync();
        var workflowIds = workflows.Select(x => x.Id).ToList();
        var triggerConfigs = workflowIds.Count == 0
            ? new List<WorkflowTriggerConfig>()
            : await _db.WorkflowTriggerConfigs.AsNoTracking()
                .Where(x => workflowIds.Contains(x.WorkflowId))
                .OrderByDescending(x => x.UpdatedAtUtc)
                .ToListAsync();
        var triggerLogs = workflowIds.Count == 0
            ? new List<WorkflowTriggerEventLog>()
            : await _db.WorkflowTriggerEventLogs.AsNoTracking()
                .Where(x => workflowIds.Contains(x.WorkflowId))
                .OrderByDescending(x => x.ProcessedAtUtc)
                .Take(12)
                .ToListAsync();
        var dynamicVeLogs = projectIds.Count == 0
            ? new List<DynamicVeAuditLog>()
            : await _db.DynamicVeAuditLogs.AsNoTracking()
                .Where(x => projectIds.Contains(x.ProjectId))
                .OrderByDescending(x => x.AtUtc)
                .Take(12)
                .ToListAsync();
        var executionLogs = await _db.WorkflowExecutionLogs.AsNoTracking()
            .Where(x => x.OwnerUserId == context.OwnerUserId)
            .OrderByDescending(x => x.ExecutedAtUtc)
            .Take(12)
            .ToListAsync();

        var analyticsPageViews7d = await BuildAnalyticsPageScope(context).Where(x => x.OccurredAtUtc >= DateTime.UtcNow.AddDays(-7)).CountAsync();
        var analyticsEvents7d = await BuildAnalyticsEventScope(context).Where(x => x.OccurredAtUtc >= DateTime.UtcNow.AddDays(-7)).CountAsync();
        var connectedProviders = connectedIntegrations.Where(x => string.Equals(x.Status, "connected", StringComparison.OrdinalIgnoreCase)).Select(x => x.Provider).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        Workspace = BuildWorkspaceSnapshot(context, teamMembers, pendingInvites, projects, domainCounts, connectedProviders, analyticsPageViews7d, analyticsEvents7d);
        Billing = BuildBillingSnapshot(subscription, paymentMethods, projects);
        ApiAccess = BuildApiAccessSnapshot(context, analyticsPageViews7d, analyticsEvents7d, workflows.Count, triggerConfigs.Count, connectedProviders);
        Webhooks = BuildWebhooksSnapshot(triggerConfigs, triggerLogs, workflows);
        Notifications = BuildNotificationsSnapshot(notificationItems, notificationTypeCounts);
        Security = await BuildSecuritySnapshotAsync(context, teamMembers, pendingInvites);
        Integrations = BuildIntegrationsSnapshot(connectedIntegrations);
        Audit = BuildAuditSnapshot(dynamicVeLogs, executionLogs, pendingInvites, projects, notificationItems);

        return Page();
    }

    private IQueryable<UploadedProject> BuildProjectScope(WorkspaceAccessContext context)
    {
        var query = _db.UploadedProjects.AsNoTracking();
        return context.CompanyId.HasValue ? query.Where(p => p.CompanyId == context.CompanyId) : query.Where(p => p.UserId == context.OwnerUserId);
    }

    private IQueryable<AnalyticsPageView> BuildAnalyticsPageScope(WorkspaceAccessContext context)
    {
        var query = _db.AnalyticsPageViews.AsNoTracking();
        return context.CompanyId.HasValue ? query.Where(x => x.CompanyId == context.CompanyId) : query.Where(x => x.OwnerUserId == context.OwnerUserId);
    }

    private IQueryable<AnalyticsEvent> BuildAnalyticsEventScope(WorkspaceAccessContext context)
    {
        var query = _db.AnalyticsEvents.AsNoTracking();
        return context.CompanyId.HasValue ? query.Where(x => x.CompanyId == context.CompanyId) : query.Where(x => x.OwnerUserId == context.OwnerUserId);
    }

    private WorkspaceSnapshot BuildWorkspaceSnapshot(WorkspaceAccessContext context, List<TeamMember> members, List<TeamInvite> invites, List<ProjectRecord> projects, Dictionary<int, int> domainCounts, int connectedProviders, int pageViews7d, int events7d)
    {
        var storageBytes = projects.Sum(x => x.SizeBytes);
        var domainCount = domainCounts.Values.Sum();
        var liveProjectCount = projects.Count(x => x.LastPublishedAtUtc.HasValue);
        var ownerName = context.OwnerUser?.GetFriendlyName();
        if (string.IsNullOrWhiteSpace(ownerName))
        {
            ownerName = context.OwnerUser?.UserName ?? "Workspace Owner";
        }

        var workspaceName = !string.IsNullOrWhiteSpace(context.Company?.Name) ? context.Company.Name : ownerName;
        var location = string.Join(", ", new[] { context.Company?.City, context.Company?.StateOrProvince, context.Company?.Country }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new WorkspaceSnapshot
        {
            Name = workspaceName,
            Subtitle = context.IsOwner ? "Primary workspace owner view." : "Shared team workspace view.",
            OwnerName = ownerName,
            OwnerEmail = context.OwnerUser?.Email ?? context.User.Email ?? "Not available",
            WorkspaceKey = context.CompanyId?.ToString() ?? $"owner-{context.OwnerUserId}",
            ScopeLabel = context.CompanyId.HasValue ? "Company workspace" : "Personal workspace",
            Location = string.IsNullOrWhiteSpace(location) ? "Location not configured" : location,
            ProjectCount = projects.Count,
            PublishedProjectCount = liveProjectCount,
            TeamCount = members.Count + 1,
            PendingInviteCount = invites.Count,
            DomainCount = domainCount,
            ConnectedProviderCount = connectedProviders,
            StorageUsedLabel = FormatBytes(storageBytes),
            PageViews7d = pageViews7d,
            Events7d = events7d,
            CanManageWorkspace = context.CanManage
        };
    }

    private BillingSnapshot BuildBillingSnapshot(UserSubscription subscription, List<UserPaymentMethod> paymentMethods, List<ProjectRecord> projects)
    {
        var storageUsed = projects.Sum(x => x.SizeBytes);
        var includedStorage = ResolveIncludedStorageBytes(subscription.PlanKey);
        var usagePercent = includedStorage <= 0 ? 0 : (int)Math.Round(Math.Clamp(storageUsed / (double)includedStorage, 0, 1) * 100);
        var methods = paymentMethods.Select(method => new PaymentMethodSnapshot
        {
            Brand = string.IsNullOrWhiteSpace(method.Brand) ? method.Provider : method.Brand!,
            Last4 = string.IsNullOrWhiteSpace(method.Last4) ? "----" : method.Last4!,
            Expiry = $"{method.ExpMonth ?? "--"}/{method.ExpYear ?? "----"}",
            Provider = method.Provider,
            IsDefault = method.IsDefault,
            AddedAtLabel = FormatRelative(method.CreatedAtUtc)
        }).ToList();
        var renewalDate = subscription.CurrentPeriodEndUtc ?? subscription.TrialEndsUtc;
        var alerts = new List<string>();
        if (methods.Count == 0) alerts.Add("No payment method is saved on the workspace owner account.");
        if (storageUsed > includedStorage * 0.8) alerts.Add("Storage usage is above 80% of the included plan allocation.");
        if (string.Equals(subscription.Status, "Pending", StringComparison.OrdinalIgnoreCase)) alerts.Add("Subscription checkout is pending confirmation.");

        return new BillingSnapshot
        {
            PlanName = subscription.PlanKey,
            Status = subscription.Status,
            IntervalLabel = ResolveIntervalLabel(subscription.Interval),
            Provider = string.IsNullOrWhiteSpace(subscription.Provider) ? "Not configured" : subscription.Provider,
            RenewalLabel = renewalDate.HasValue ? renewalDate.Value.ToLocalTime().ToString("MMM d, yyyy") : "Not scheduled",
            EstimatedChargeLabel = ResolvePlanPriceLabel(subscription.PlanKey, subscription.Interval),
            StorageUsedLabel = FormatBytes(storageUsed),
            StorageLimitLabel = includedStorage > 0 ? FormatBytes(includedStorage) : "Custom",
            UsagePercent = usagePercent,
            PaymentMethods = methods,
            Alerts = alerts
        };
    }

    private ApiAccessSnapshot BuildApiAccessSnapshot(WorkspaceAccessContext context, int pageViews7d, int events7d, int workflowCount, int triggerCount, int connectedProviders)
    {
        var origin = $"{Request.Scheme}://{Request.Host}";
        return new ApiAccessSnapshot
        {
            WorkspaceScope = context.CompanyId.HasValue ? $"Company scoped to {context.CompanyId}" : $"Owner scoped to {context.OwnerUserId}",
            BaseOrigin = origin,
            TokenCount = 0,
            PageViews7d = pageViews7d,
            Events7d = events7d,
            WorkflowCount = workflowCount,
            TriggerCount = triggerCount,
            ConnectedProviderCount = connectedProviders,
            Surfaces = new List<ApiSurfaceSnapshot>
            {
                new() { Name = "Workspace Origin", Value = origin, Summary = "Primary base URL used by dashboard, portal, and settings routes.", Status = "Live" },
                new() { Name = "Portal Delivery Surface", Value = $"{origin}/Portal/Page={{id}}", Summary = "Dynamic page rendering route for live portal delivery and admin overlays.", Status = "Active" },
                new() { Name = "Automation Surface", Value = $"{workflowCount} workflows / {triggerCount} triggers", Summary = "Workflow engine and trigger configuration currently active for this workspace.", Status = workflowCount > 0 ? "Configured" : "Empty" },
                new() { Name = "API Token Registry", Value = "No workspace token store yet", Summary = "The workspace can expose token management here once a token table is introduced.", Status = "Foundation ready" }
            }
        };
    }

    private WebhooksSnapshot BuildWebhooksSnapshot(List<WorkflowTriggerConfig> triggerConfigs, List<WorkflowTriggerEventLog> triggerLogs, List<Workflow> workflows)
    {
        var workflowMap = workflows.ToDictionary(x => x.Id, x => x.Name);
        var deliveries = triggerLogs.Select(log => new WebhookDeliverySnapshot
        {
            WorkflowName = workflowMap.TryGetValue(log.WorkflowId, out var workflowName) ? workflowName : "Workflow",
            Provider = log.Provider,
            Outcome = log.Outcome,
            Mode = log.Mode,
            ProcessedAtLabel = FormatRelative(log.ProcessedAtUtc)
        }).ToList();
        var processedCount = triggerLogs.Count(x => string.Equals(x.Outcome, "processed", StringComparison.OrdinalIgnoreCase));
        var successRate = triggerLogs.Count == 0 ? 0 : (int)Math.Round(processedCount / (double)triggerLogs.Count * 100);
        var sources = triggerConfigs.Select(config => new WebhookSourceSnapshot
        {
            WorkflowName = workflowMap.TryGetValue(config.WorkflowId, out var workflowName) ? workflowName : "Workflow",
            TriggerType = config.TriggerType,
            EventName = config.TriggerEvent,
            Mode = config.Mode,
            UpdatedAtLabel = FormatRelative(config.UpdatedAtUtc)
        }).ToList();
        return new WebhooksSnapshot { ConfiguredSourceCount = triggerConfigs.Count, DeliveryCount = triggerLogs.Count, SuccessRate = successRate, Sources = sources, Deliveries = deliveries };
    }

    private NotificationsSnapshot BuildNotificationsSnapshot(List<UserNotification> items, Dictionary<string, int> typeCounts)
    {
        return new NotificationsSnapshot
        {
            UnreadCount = items.Count(x => !x.IsRead),
            TotalCount = items.Count,
            InfoCount = GetCount(typeCounts, "info"),
            SuccessCount = GetCount(typeCounts, "success"),
            WarningCount = GetCount(typeCounts, "warning"),
            ErrorCount = GetCount(typeCounts, "error"),
            Recent = items.Select(item => new NotificationItemSnapshot
            {
                Title = item.Title,
                Message = item.Message,
                Type = string.IsNullOrWhiteSpace(item.Type) ? "info" : item.Type,
                IsRead = item.IsRead,
                TimeLabel = FormatRelative(item.CreatedAtUtc)
            }).ToList()
        };
    }

    private async Task<SecuritySnapshot> BuildSecuritySnapshotAsync(WorkspaceAccessContext context, List<TeamMember> teamMembers, List<TeamInvite> pendingInvites)
    {
        var ownerUser = context.OwnerUser ?? context.User;
        var externalLogins = await _userManager.GetLoginsAsync(ownerUser);
        var resetRequests30d = string.IsNullOrWhiteSpace(ownerUser.Email) ? 0 : await _db.PasswordResetTickets.AsNoTracking().Where(x => x.Email == ownerUser.Email && x.CreatedAtUtc >= DateTime.UtcNow.AddDays(-30)).CountAsync();
        var otpRequests7d = string.IsNullOrWhiteSpace(ownerUser.Email) ? 0 : await _db.EmailOtpTickets.AsNoTracking().Where(x => x.Email == ownerUser.Email && x.CreatedAtUtc >= DateTime.UtcNow.AddDays(-7)).CountAsync();
        return new SecuritySnapshot
        {
            EmailConfirmed = ownerUser.EmailConfirmed,
            TwoFactorEnabled = ownerUser.TwoFactorEnabled,
            PhoneConfigured = !string.IsNullOrWhiteSpace(ownerUser.PhoneNumber),
            ExternalLoginCount = externalLogins.Count,
            AdminCount = teamMembers.Count(x => string.Equals(x.Role, "Admin", StringComparison.OrdinalIgnoreCase)) + 1,
            ActiveMemberCount = teamMembers.Count(x => string.Equals(x.Status, "Active", StringComparison.OrdinalIgnoreCase)) + 1,
            PendingInviteCount = pendingInvites.Count,
            PasswordResetRequests30d = resetRequests30d,
            OtpRequests7d = otpRequests7d,
            SsoStatus = "Not configured",
            RecoveryStatus = ownerUser.EmailConfirmed ? "Verified recovery email present" : "Recovery email still needs verification"
        };
    }

    private IntegrationsSnapshot BuildIntegrationsSnapshot(List<IntegrationConnection> connections)
    {
        var connectionMap = connections.GroupBy(x => x.Provider, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.UpdatedAtUtc).First(), StringComparer.OrdinalIgnoreCase);
        var cards = new List<IntegrationCardSnapshot>
        {
            BuildIntegrationCard(connectionMap, "facebook", "Facebook", "fa-brands fa-facebook-f", "#1877f2", "Leads and pages", "/Settings/Integration"),
            BuildIntegrationCard(connectionMap, "instagram", "Instagram", "fa-brands fa-instagram", "#f77737", "Social identity", "/Settings/Integration"),
            BuildIntegrationCard(connectionMap, "linkedin", "LinkedIn", "fa-brands fa-linkedin-in", "#0a66c2", "Professional publishing", "/Settings/Integration"),
            BuildIntegrationCard(connectionMap, "github", "GitHub", "fa-brands fa-github", "#8b5cf6", "Code and deployment flows", "/Settings/Integration"),
            BuildIntegrationCard(connectionMap, "whatsapp", "WhatsApp", "fa-brands fa-whatsapp", "#25d366", "Messaging touchpoints", "/Settings/Integration"),
            BuildIntegrationCard(connectionMap, "google", "Google Search Console", "fa-brands fa-google", "#4285f4", "Search visibility", "/Settings/Integration"),
            BuildPlannedCard("Slack", "fa-brands fa-slack", "#4a154b", "Ops alerts and approvals", "/Settings/Integration"),
            BuildPlannedCard("HubSpot", "fa-brands fa-hubspot", "#f97316", "CRM handoff and attribution", "/Settings/Integration")
        };
        return new IntegrationsSnapshot { ConnectedCount = cards.Count(x => x.IsConnected), Cards = cards };
    }

    private AuditSnapshot BuildAuditSnapshot(List<DynamicVeAuditLog> dynamicVeLogs, List<WorkflowExecutionLog> executionLogs, List<TeamInvite> invites, List<ProjectRecord> projects, List<UserNotification> notifications)
    {
        var projectMap = projects.ToDictionary(x => x.Id, x => string.IsNullOrWhiteSpace(x.DisplayName) ? x.FolderName : x.DisplayName!, EqualityComparer<int>.Default);
        var items = new List<AuditItemSnapshot>();
        items.AddRange(dynamicVeLogs.Select(log => new AuditItemSnapshot { Category = "dynamicve", Title = log.Action, Target = projectMap.TryGetValue(log.ProjectId, out var projectName) ? projectName : $"Project #{log.ProjectId}", Meta = "Dynamic VE audit", TimeUtc = log.AtUtc, Severity = "accent" }));
        items.AddRange(executionLogs.Select(log => new AuditItemSnapshot { Category = "workflow", Title = log.Status, Target = string.IsNullOrWhiteSpace(log.StepName) ? log.Message : log.StepName!, Meta = "Workflow execution", TimeUtc = log.ExecutedAtUtc, Severity = string.Equals(log.Status, "Success", StringComparison.OrdinalIgnoreCase) ? "success" : "warning" }));
        items.AddRange(invites.Select(invite => new AuditItemSnapshot { Category = "team", Title = "Invite issued", Target = invite.Email, Meta = $"Role: {invite.Role}", TimeUtc = invite.CreatedAtUtc, Severity = "warning" }));
        items.AddRange(projects.Select(project => new AuditItemSnapshot { Category = "project", Title = project.LastPublishedAtUtc.HasValue ? "Project published" : "Project uploaded", Target = string.IsNullOrWhiteSpace(project.DisplayName) ? project.FolderName : project.DisplayName!, Meta = project.LastPublishedAtUtc.HasValue ? "Delivery" : "Storage", TimeUtc = project.LastPublishedAtUtc ?? project.UploadedAtUtc, Severity = project.LastPublishedAtUtc.HasValue ? "success" : "neutral" }));
        items.AddRange(notifications.Select(item => new AuditItemSnapshot { Category = "notification", Title = item.Title, Target = item.Type, Meta = "Notification center", TimeUtc = item.CreatedAtUtc, Severity = item.IsRead ? "neutral" : "accent" }));
        var ordered = items.OrderByDescending(x => x.TimeUtc).Take(24).ToList();
        return new AuditSnapshot { TotalCount = ordered.Count, DynamicVeCount = ordered.Count(x => x.Category == "dynamicve"), WorkflowCount = ordered.Count(x => x.Category == "workflow"), TeamCount = ordered.Count(x => x.Category == "team"), ProjectCount = ordered.Count(x => x.Category == "project"), Items = ordered };
    }

    private static IntegrationCardSnapshot BuildIntegrationCard(IReadOnlyDictionary<string, IntegrationConnection> connectionMap, string key, string name, string iconClass, string accent, string summary, string actionUrl)
    {
        var connected = connectionMap.TryGetValue(key, out var connection) && string.Equals(connection.Status, "connected", StringComparison.OrdinalIgnoreCase);
        return new IntegrationCardSnapshot
        {
            Name = name,
            IconClass = iconClass,
            Accent = accent,
            Summary = summary,
            ActionUrl = actionUrl,
            ActionLabel = connected ? "Manage" : "Connect",
            Status = connected ? "Connected" : "Available",
            StatusMeta = connected ? $"{connection!.DisplayName} • updated {FormatRelative(connection.UpdatedAtUtc)}" : "No active connection for this workspace",
            IsConnected = connected
        };
    }

    private static IntegrationCardSnapshot BuildPlannedCard(string name, string iconClass, string accent, string summary, string actionUrl)
    {
        return new IntegrationCardSnapshot { Name = name, IconClass = iconClass, Accent = accent, Summary = summary, ActionUrl = actionUrl, ActionLabel = "Open setup", Status = "Planned", StatusMeta = "Foundation slot is ready for a deeper provider implementation." };
    }

    private async Task<WorkspaceAccessContext?> GetAccessContextAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;

        var currentMember = await _db.TeamMembers.AsNoTracking()
            .Where(m => m.UserId == user.Id && m.Status == "Active")
            .OrderByDescending(m => m.JoinedAtUtc)
            .FirstOrDefaultAsync();

        var isOwner = true;
        var ownerUserId = user.Id;
        var canManage = true;
        ApplicationUser? ownerUser = user;
        if (currentMember is not null)
        {
            var ownerCandidate = await _userManager.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentMember.OwnerUserId);
            if (ownerCandidate is not null && ownerCandidate.CompanyId == user.CompanyId)
            {
                ownerUser = ownerCandidate;
                ownerUserId = currentMember.OwnerUserId;
                isOwner = false;
                canManage = string.Equals(currentMember.Role, "Admin", StringComparison.OrdinalIgnoreCase);
            }
        }

        var companyId = ownerUser?.CompanyId ?? user.CompanyId;
        CompanyProfile? company = null;
        if (companyId.HasValue)
        {
            company = await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == companyId.Value);
        }

        return new WorkspaceAccessContext { User = user, OwnerUser = ownerUser, OwnerUserId = ownerUserId, CompanyId = companyId, Company = company, IsOwner = isOwner, CanManage = canManage };
    }

    private static string ResolveEnvironment(ProjectEnvironmentPointer? pointer, DateTime? lastPublishedAtUtc)
    {
        if (pointer?.LiveSnapshotId != null) return "Production";
        if (pointer?.StagingSnapshotId != null) return "Staging";
        if (pointer?.DraftSnapshotId != null) return "Draft";
        return lastPublishedAtUtc.HasValue ? "Published" : "Upload only";
    }

    private static long ResolveIncludedStorageBytes(string? planKey) => (planKey ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "starter" => 100L * 1024 * 1024 * 1024,
        "pro" => 500L * 1024 * 1024 * 1024,
        "business" => 1024L * 1024 * 1024 * 1024,
        "enterprise" => 5L * 1024 * 1024 * 1024 * 1024,
        _ => 100L * 1024 * 1024 * 1024
    };

    private static string ResolvePlanPriceLabel(string? planKey, SubscriptionInterval interval)
    {
        var monthly = (planKey ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "starter" => 29,
            "pro" => 99,
            "business" => 249,
            "enterprise" => 999,
            _ => 29
        };
        return interval switch
        {
            SubscriptionInterval.Yearly => $"${monthly * 10}/mo billed yearly",
            SubscriptionInterval.SixMonths => $"${monthly * 0.9:0}/mo billed every 6 months",
            SubscriptionInterval.Trial => "Trial period active",
            _ => $"${monthly}/mo"
        };
    }

    private static string ResolveIntervalLabel(SubscriptionInterval interval) => interval switch
    {
        SubscriptionInterval.Trial => "Trial",
        SubscriptionInterval.Monthly => "Monthly",
        SubscriptionInterval.SixMonths => "6 months",
        SubscriptionInterval.Yearly => "Yearly",
        _ => "Custom"
    };

    private static int GetCount(IReadOnlyDictionary<string, int> counts, string key) => counts.TryGetValue(key, out var value) ? value : 0;

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double size = Math.Max(0, bytes);
        var order = 0;
        while (size >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return order == 0 ? $"{size:0} {suffixes[order]}" : $"{size:0.#} {suffixes[order]}";
    }

    private static string FormatRelative(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{Math.Max(1, (int)span.TotalMinutes)} min ago";
        if (span.TotalDays < 1) return $"{Math.Max(1, (int)span.TotalHours)} hr ago";
        if (span.TotalDays < 30) return $"{Math.Max(1, (int)span.TotalDays)} days ago";
        return utc.ToLocalTime().ToString("MMM d, yyyy");
    }

    private sealed class WorkspaceAccessContext
    {
        public required ApplicationUser User { get; init; }
        public ApplicationUser? OwnerUser { get; init; }
        public required string OwnerUserId { get; init; }
        public Guid? CompanyId { get; init; }
        public CompanyProfile? Company { get; init; }
        public bool IsOwner { get; init; }
        public bool CanManage { get; init; }
    }

    private sealed class ProjectRecord
    {
        public int Id { get; set; }
        public string FolderName { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string Slug { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime UploadedAtUtc { get; set; }
        public DateTime? LastPublishedAtUtc { get; set; }
    }

    public sealed class WorkspaceSnapshot { public string Name { get; set; } = "Workspace"; public string Subtitle { get; set; } = string.Empty; public string OwnerName { get; set; } = string.Empty; public string OwnerEmail { get; set; } = string.Empty; public string WorkspaceKey { get; set; } = string.Empty; public string ScopeLabel { get; set; } = string.Empty; public string Location { get; set; } = string.Empty; public int ProjectCount { get; set; } public int PublishedProjectCount { get; set; } public int TeamCount { get; set; } public int PendingInviteCount { get; set; } public int DomainCount { get; set; } public int ConnectedProviderCount { get; set; } public string StorageUsedLabel { get; set; } = "0 B"; public int PageViews7d { get; set; } public int Events7d { get; set; } public bool CanManageWorkspace { get; set; } }
    public sealed class ProjectOption { public int Id { get; set; } public string Name { get; set; } = string.Empty; public string Slug { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public long SizeBytes { get; set; } public string SizeLabel { get; set; } = "0 B"; public DateTime UploadedAtUtc { get; set; } public DateTime? LastPublishedAtUtc { get; set; } public int FileCount { get; set; } public int DomainCount { get; set; } public int? PreflightScore { get; set; } public bool? PreflightSafe { get; set; } public string EnvironmentLabel { get; set; } = string.Empty; public string ActivityLabel { get; set; } = string.Empty; }
    public sealed class BillingSnapshot { public string PlanName { get; set; } = "Starter"; public string Status { get; set; } = "Trial"; public string IntervalLabel { get; set; } = "Monthly"; public string Provider { get; set; } = string.Empty; public string RenewalLabel { get; set; } = string.Empty; public string EstimatedChargeLabel { get; set; } = string.Empty; public string StorageUsedLabel { get; set; } = "0 B"; public string StorageLimitLabel { get; set; } = "0 B"; public int UsagePercent { get; set; } public List<PaymentMethodSnapshot> PaymentMethods { get; set; } = new(); public List<string> Alerts { get; set; } = new(); }
    public sealed class PaymentMethodSnapshot { public string Brand { get; set; } = string.Empty; public string Last4 { get; set; } = string.Empty; public string Expiry { get; set; } = string.Empty; public string Provider { get; set; } = string.Empty; public bool IsDefault { get; set; } public string AddedAtLabel { get; set; } = string.Empty; }
    public sealed class ApiAccessSnapshot { public string WorkspaceScope { get; set; } = string.Empty; public string BaseOrigin { get; set; } = string.Empty; public int TokenCount { get; set; } public int PageViews7d { get; set; } public int Events7d { get; set; } public int WorkflowCount { get; set; } public int TriggerCount { get; set; } public int ConnectedProviderCount { get; set; } public List<ApiSurfaceSnapshot> Surfaces { get; set; } = new(); }
    public sealed class ApiSurfaceSnapshot { public string Name { get; set; } = string.Empty; public string Value { get; set; } = string.Empty; public string Summary { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; }
    public sealed class WebhooksSnapshot { public int ConfiguredSourceCount { get; set; } public int DeliveryCount { get; set; } public int SuccessRate { get; set; } public List<WebhookSourceSnapshot> Sources { get; set; } = new(); public List<WebhookDeliverySnapshot> Deliveries { get; set; } = new(); }
    public sealed class WebhookSourceSnapshot { public string WorkflowName { get; set; } = string.Empty; public string TriggerType { get; set; } = string.Empty; public string EventName { get; set; } = string.Empty; public string Mode { get; set; } = string.Empty; public string UpdatedAtLabel { get; set; } = string.Empty; }
    public sealed class WebhookDeliverySnapshot { public string WorkflowName { get; set; } = string.Empty; public string Provider { get; set; } = string.Empty; public string Outcome { get; set; } = string.Empty; public string Mode { get; set; } = string.Empty; public string ProcessedAtLabel { get; set; } = string.Empty; }
    public sealed class NotificationsSnapshot { public int UnreadCount { get; set; } public int TotalCount { get; set; } public int InfoCount { get; set; } public int SuccessCount { get; set; } public int WarningCount { get; set; } public int ErrorCount { get; set; } public List<NotificationItemSnapshot> Recent { get; set; } = new(); }
    public sealed class NotificationItemSnapshot { public string Title { get; set; } = string.Empty; public string Message { get; set; } = string.Empty; public string Type { get; set; } = string.Empty; public bool IsRead { get; set; } public string TimeLabel { get; set; } = string.Empty; }
    public sealed class SecuritySnapshot { public bool EmailConfirmed { get; set; } public bool TwoFactorEnabled { get; set; } public bool PhoneConfigured { get; set; } public int ExternalLoginCount { get; set; } public int AdminCount { get; set; } public int ActiveMemberCount { get; set; } public int PendingInviteCount { get; set; } public int PasswordResetRequests30d { get; set; } public int OtpRequests7d { get; set; } public string SsoStatus { get; set; } = string.Empty; public string RecoveryStatus { get; set; } = string.Empty; }
    public sealed class IntegrationsSnapshot { public int ConnectedCount { get; set; } public List<IntegrationCardSnapshot> Cards { get; set; } = new(); }
    public sealed class IntegrationCardSnapshot { public string Name { get; set; } = string.Empty; public string IconClass { get; set; } = string.Empty; public string Accent { get; set; } = "#06b6d4"; public string Summary { get; set; } = string.Empty; public string ActionUrl { get; set; } = string.Empty; public string ActionLabel { get; set; } = "Open"; public string Status { get; set; } = string.Empty; public string StatusMeta { get; set; } = string.Empty; public bool IsConnected { get; set; } }
    public sealed class AuditSnapshot { public int TotalCount { get; set; } public int DynamicVeCount { get; set; } public int WorkflowCount { get; set; } public int TeamCount { get; set; } public int ProjectCount { get; set; } public List<AuditItemSnapshot> Items { get; set; } = new(); }
    public sealed class AuditItemSnapshot { public string Category { get; set; } = string.Empty; public string Title { get; set; } = string.Empty; public string Target { get; set; } = string.Empty; public string Meta { get; set; } = string.Empty; public DateTime TimeUtc { get; set; } public string Severity { get; set; } = "neutral"; }
}
