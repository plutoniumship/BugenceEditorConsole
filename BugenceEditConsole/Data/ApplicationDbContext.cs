using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<SitePage> SitePages => Set<SitePage>();
    public DbSet<PageSection> PageSections => Set<PageSection>();
    public DbSet<ContentChangeLog> ContentChangeLogs => Set<ContentChangeLog>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<PasswordResetTicket> PasswordResetTickets => Set<PasswordResetTicket>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<UploadedProject> UploadedProjects => Set<UploadedProject>();
    public DbSet<UploadedProjectFile> UploadedProjectFiles => Set<UploadedProjectFile>();
    public DbSet<PreviousDeploy> PreviousDeploys => Set<PreviousDeploy>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<UserSubscription> UserSubscriptions => Set<UserSubscription>();
    public DbSet<UserPaymentMethod> UserPaymentMethods => Set<UserPaymentMethod>();
    public DbSet<EmailOtpTicket> EmailOtpTickets => Set<EmailOtpTicket>();
    public DbSet<ProjectDomain> ProjectDomains => Set<ProjectDomain>();
    public DbSet<ProjectDomainDnsRecord> ProjectDomainDnsRecords => Set<ProjectDomainDnsRecord>();
    public DbSet<DomainVerificationLog> DomainVerificationLogs => Set<DomainVerificationLog>();
    public DbSet<AnalyticsSession> AnalyticsSessions => Set<AnalyticsSession>();
    public DbSet<AnalyticsPageView> AnalyticsPageViews => Set<AnalyticsPageView>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    public DbSet<TeamInvite> TeamInvites => Set<TeamInvite>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<WorkflowExecutionLog> WorkflowExecutionLogs => Set<WorkflowExecutionLog>();
    public DbSet<ProjectDeploySnapshot> ProjectDeploySnapshots => Set<ProjectDeploySnapshot>();
    public DbSet<ProjectPreflightRun> ProjectPreflightRuns => Set<ProjectPreflightRun>();
    public DbSet<ProjectEnvironmentPointer> ProjectEnvironmentPointers => Set<ProjectEnvironmentPointer>();
    public DbSet<DynamicVeProjectConfig> DynamicVeProjectConfigs => Set<DynamicVeProjectConfig>();
    public DbSet<DynamicVePageRevision> DynamicVePageRevisions => Set<DynamicVePageRevision>();
    public DbSet<DynamicVeElementMap> DynamicVeElementMaps => Set<DynamicVeElementMap>();
    public DbSet<DynamicVePatchRule> DynamicVePatchRules => Set<DynamicVePatchRule>();
    public DbSet<DynamicVeTextPatch> DynamicVeTextPatches => Set<DynamicVeTextPatch>();
    public DbSet<DynamicVeSectionInstance> DynamicVeSectionInstances => Set<DynamicVeSectionInstance>();
    public DbSet<DynamicVeActionBinding> DynamicVeActionBindings => Set<DynamicVeActionBinding>();
    public DbSet<DynamicVePublishArtifact> DynamicVePublishArtifacts => Set<DynamicVePublishArtifact>();
    public DbSet<DynamicVeAuditLog> DynamicVeAuditLogs => Set<DynamicVeAuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<SitePage>()
            .HasIndex(p => p.Slug)
            .IsUnique();

        builder.Entity<PageSection>()
            .HasIndex(s => new { s.SitePageId, s.SectionKey })
            .IsUnique();

        builder.Entity<PageSection>()
            .HasOne(s => s.SitePage)
            .WithMany(p => p.Sections)
            .HasForeignKey(s => s.SitePageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ContentChangeLog>()
            .HasIndex(c => c.PerformedAtUtc);

        builder.Entity<UploadedProject>()
            .HasIndex(p => p.Slug)
            .IsUnique();

        builder.Entity<UploadedProject>()
            .HasIndex(p => new { p.CompanyId, p.UploadedAtUtc });

        builder.Entity<CompanyProfile>()
            .HasIndex(c => c.Name);

        builder.Entity<ApplicationUser>()
            .HasIndex(u => u.CompanyId);

        builder.Entity<ApplicationUser>()
            .HasOne(u => u.Company)
            .WithMany(c => c.Users)
            .HasForeignKey(u => u.CompanyId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<UploadedProject>()
            .HasOne(p => p.Company)
            .WithMany(c => c.UploadedProjects)
            .HasForeignKey(p => p.CompanyId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<UserProfile>()
            .HasIndex(p => p.UserId)
            .IsUnique();

        builder.Entity<PasswordResetTicket>()
            .HasIndex(t => new { t.Email, t.VerificationCode });

        builder.Entity<PasswordResetTicket>()
            .HasIndex(t => t.ExpiresAtUtc);

        builder.Entity<UserNotification>()
            .HasIndex(n => new { n.UserId, n.CreatedAtUtc });

        builder.Entity<UserSubscription>()
            .HasIndex(s => new { s.UserId, s.Status });

        builder.Entity<UserPaymentMethod>()
            .HasIndex(p => new { p.UserId, p.IsDefault });

        builder.Entity<EmailOtpTicket>()
            .HasIndex(t => new { t.Email, t.Purpose, t.ExpiresAtUtc });

        builder.Entity<TeamInvite>()
            .HasIndex(t => new { t.OwnerUserId, t.Email })
            .IsUnique();

        builder.Entity<TeamInvite>()
            .HasIndex(t => new { t.OwnerUserId, t.ExpiresAtUtc });

        builder.Entity<TeamMember>()
            .HasIndex(t => new { t.OwnerUserId, t.Email })
            .IsUnique();

        builder.Entity<Workflow>()
            .HasIndex(w => new { w.OwnerUserId, w.CreatedAtUtc });

        builder.Entity<Workflow>()
            .HasIndex(w => new { w.CompanyId, w.CreatedAtUtc });

        builder.Entity<Workflow>()
            .HasIndex(w => new { w.OwnerUserId, w.DisplayId })
            .IsUnique();

        builder.Entity<Workflow>()
            .HasIndex(w => new { w.OwnerUserId, w.Dguid })
            .IsUnique();

        builder.Entity<Workflow>()
            .HasOne<CompanyProfile>()
            .WithMany()
            .HasForeignKey(w => w.CompanyId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<WorkflowExecutionLog>()
            .HasIndex(l => new { l.WorkflowId, l.ExecutedAtUtc });

        builder.Entity<ProjectDomain>()
            .HasIndex(d => d.DomainName)
            .IsUnique();

        builder.Entity<ProjectDomain>()
            .HasIndex(d => d.NormalizedDomain)
            .IsUnique();

        builder.Entity<ProjectDomain>()
            .HasIndex(d => d.ConsecutiveFailureCount);

        builder.Entity<ProjectDomainDnsRecord>()
            .HasIndex(r => new { r.ProjectDomainId, r.RecordType, r.Name, r.Purpose })
            .IsUnique();

        builder.Entity<ProjectDomain>()
            .HasOne(d => d.Project)
            .WithMany(p => p.Domains)
            .HasForeignKey(d => d.UploadedProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProjectDomainDnsRecord>()
            .HasOne(r => r.Domain)
            .WithMany(d => d.DnsRecords)
            .HasForeignKey(r => r.ProjectDomainId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<DomainVerificationLog>()
            .HasIndex(l => new { l.ProjectDomainId, l.CheckedAtUtc });

        builder.Entity<DomainVerificationLog>()
            .HasIndex(l => l.FailureStreak);

        builder.Entity<DomainVerificationLog>()
            .HasOne(l => l.Domain)
            .WithMany()
            .HasForeignKey(l => l.ProjectDomainId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<AnalyticsSession>()
            .HasIndex(s => new { s.OwnerUserId, s.CompanyId, s.ProjectId, s.LastSeenUtc });

        builder.Entity<AnalyticsSession>()
            .HasIndex(s => s.SessionId)
            .IsUnique();

        builder.Entity<AnalyticsPageView>()
            .HasIndex(v => new { v.OwnerUserId, v.CompanyId, v.ProjectId, v.OccurredAtUtc });

        builder.Entity<AnalyticsPageView>()
            .HasIndex(v => new { v.ProjectId, v.Path, v.OccurredAtUtc });

        builder.Entity<AnalyticsEvent>()
            .HasIndex(e => new { e.OwnerUserId, e.CompanyId, e.ProjectId, e.OccurredAtUtc });

        builder.Entity<AnalyticsEvent>()
            .HasIndex(e => new { e.ProjectId, e.EventName, e.OccurredAtUtc });

        builder.Entity<ProjectDeploySnapshot>()
            .HasIndex(s => new { s.UploadedProjectId, s.Environment, s.CreatedAtUtc });

        builder.Entity<ProjectDeploySnapshot>()
            .HasOne(s => s.Project)
            .WithMany(p => p.DeploySnapshots)
            .HasForeignKey(s => s.UploadedProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProjectPreflightRun>()
            .HasIndex(r => new { r.UploadedProjectId, r.CreatedAtUtc });

        builder.Entity<ProjectPreflightRun>()
            .HasOne(r => r.Project)
            .WithMany(p => p.PreflightRuns)
            .HasForeignKey(r => r.UploadedProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProjectEnvironmentPointer>()
            .HasIndex(p => p.UploadedProjectId)
            .IsUnique();

        builder.Entity<ProjectEnvironmentPointer>()
            .HasOne(p => p.Project)
            .WithOne(p => p.EnvironmentPointer)
            .HasForeignKey<ProjectEnvironmentPointer>(p => p.UploadedProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProjectEnvironmentPointer>()
            .HasOne(p => p.DraftSnapshot)
            .WithMany()
            .HasForeignKey(p => p.DraftSnapshotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<ProjectEnvironmentPointer>()
            .HasOne(p => p.StagingSnapshot)
            .WithMany()
            .HasForeignKey(p => p.StagingSnapshotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<ProjectEnvironmentPointer>()
            .HasOne(p => p.LiveSnapshot)
            .WithMany()
            .HasForeignKey(p => p.LiveSnapshotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<DynamicVeProjectConfig>()
            .HasIndex(x => x.UploadedProjectId)
            .IsUnique();

        builder.Entity<DynamicVeProjectConfig>()
            .HasOne(x => x.Project)
            .WithOne(x => x.DynamicVeConfig)
            .HasForeignKey<DynamicVeProjectConfig>(x => x.UploadedProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<DynamicVePageRevision>()
            .HasIndex(x => new { x.UploadedProjectId, x.PagePath, x.CreatedAtUtc });

        builder.Entity<DynamicVePageRevision>()
            .HasOne(x => x.Project)
            .WithMany(x => x.DynamicVeRevisions)
            .HasForeignKey(x => x.UploadedProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<DynamicVeElementMap>()
            .HasIndex(x => new { x.RevisionId, x.ElementKey });

        builder.Entity<DynamicVeElementMap>()
            .Property(x => x.LastResolvedSelector)
            .HasMaxLength(1024);

        builder.Entity<DynamicVeElementMap>()
            .HasOne(x => x.Revision)
            .WithMany()
            .HasForeignKey(x => x.RevisionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<DynamicVePatchRule>()
            .HasIndex(x => new { x.RevisionId, x.ElementKey, x.Breakpoint, x.State });

        builder.Entity<DynamicVePatchRule>()
            .HasOne(x => x.Revision)
            .WithMany()
            .HasForeignKey(x => x.RevisionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<DynamicVeTextPatch>()
            .HasIndex(x => new { x.RevisionId, x.ElementKey });

        builder.Entity<DynamicVeTextPatch>()
            .HasOne(x => x.Revision)
            .WithMany()
            .HasForeignKey(x => x.RevisionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<DynamicVeSectionInstance>()
            .HasIndex(x => new { x.RevisionId, x.TemplateId });

        builder.Entity<DynamicVeSectionInstance>()
            .HasOne(x => x.Revision)
            .WithMany()
            .HasForeignKey(x => x.RevisionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<DynamicVeActionBinding>()
            .HasIndex(x => new { x.RevisionId, x.ElementKey });

        builder.Entity<DynamicVeActionBinding>()
            .HasOne(x => x.Revision)
            .WithMany()
            .HasForeignKey(x => x.RevisionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<DynamicVePublishArtifact>()
            .HasIndex(x => new { x.RevisionId, x.PublishedAtUtc });

        builder.Entity<DynamicVePublishArtifact>()
            .HasOne(x => x.Revision)
            .WithMany()
            .HasForeignKey(x => x.RevisionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<DynamicVeAuditLog>()
            .HasIndex(x => new { x.ProjectId, x.AtUtc });
    }
}
