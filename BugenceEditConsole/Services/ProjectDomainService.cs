using System.Security.Cryptography;
using BugenceEditConsole.Data;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BugenceEditConsole.Services;

public interface IProjectDomainService
{
    Task<ProjectDomain> EnsurePrimaryDomainAsync(int projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectDomain>> GetDomainsAsync(int projectId, CancellationToken cancellationToken = default);
    Task<ProjectDomain> AddCustomDomainAsync(int projectId, string domainName, CancellationToken cancellationToken = default);
    Task RemoveDomainAsync(int projectId, Guid domainId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectDomainDnsRecord>> GetDnsRecordsAsync(Guid domainId, CancellationToken cancellationToken = default);
}

public class ProjectDomainService : IProjectDomainService
{
    private readonly ApplicationDbContext _db;
    private readonly DomainRoutingOptions _options;
    private readonly IDomainRouter _router;
    private readonly IIisDomainBindingService _iisBindings;
    private readonly IIisProjectSiteService _projectSites;
    private readonly ILogger<ProjectDomainService> _logger;

    public ProjectDomainService(
        ApplicationDbContext db,
        IOptions<DomainRoutingOptions> options,
        IDomainRouter router,
        IIisDomainBindingService iisBindings,
        IIisProjectSiteService projectSites,
        ILogger<ProjectDomainService> logger)
    {
        _db = db;
        _options = options.Value;
        _router = router;
        _iisBindings = iisBindings;
        _projectSites = projectSites;
        _logger = logger;
    }

    public async Task<ProjectDomain> EnsurePrimaryDomainAsync(int projectId, CancellationToken cancellationToken = default)
    {
        var project = await _db.UploadedProjects
            .Include(p => p.Domains.Where(d => d.DomainType == ProjectDomainType.Primary))
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");

        if (string.IsNullOrWhiteSpace(project.Slug))
        {
            project.Slug = SlugGenerator.Slugify(project.DisplayName ?? project.FolderName ?? project.Id.ToString());
            await _db.SaveChangesAsync(cancellationToken);
        }

        var desiredDomain = BuildPrimaryDomain(project.Slug);

        var primary = project.Domains.FirstOrDefault();
        if (primary is not null && string.Equals(primary.DomainName, desiredDomain, StringComparison.OrdinalIgnoreCase))
        {
            return primary;
        }

        if (primary is not null)
        {
            _db.ProjectDomains.Remove(primary);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var domain = new ProjectDomain
        {
            Id = Guid.NewGuid(),
            UploadedProjectId = project.Id,
            DomainName = desiredDomain,
            NormalizedDomain = DomainUtilities.Normalize(desiredDomain),
            ApexRoot = DomainUtilities.GetApex(desiredDomain),
            DomainType = ProjectDomainType.Primary,
            Status = DomainStatus.Connected,
            SslStatus = DomainSslStatus.Active,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            VerifiedAtUtc = DateTime.UtcNow,
            LastCheckedAtUtc = DateTime.UtcNow,
            LastSslRenewalAtUtc = DateTime.UtcNow
        };

        _db.ProjectDomains.Add(domain);
        await _db.SaveChangesAsync(cancellationToken);
        return domain;
    }

    public async Task<IReadOnlyList<ProjectDomain>> GetDomainsAsync(int projectId, CancellationToken cancellationToken = default)
    {
        await EnsurePrimaryDomainAsync(projectId, cancellationToken);

        return await _db.ProjectDomains
            .AsNoTracking()
            .Include(d => d.DnsRecords)
            .Where(d => d.UploadedProjectId == projectId)
            .OrderBy(d => d.DomainType)
            .ThenBy(d => d.DomainName)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProjectDomain> AddCustomDomainAsync(int projectId, string domainName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(domainName))
        {
            throw new InvalidOperationException("Domain name is required.");
        }

        var normalized = DomainUtilities.Normalize(domainName);
        if (string.IsNullOrWhiteSpace(normalized) || !normalized.Contains('.', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Domain name is invalid.");
        }

        await EnsurePrimaryDomainAsync(projectId, cancellationToken);

        var exists = await _db.ProjectDomains
            .AnyAsync(d => d.NormalizedDomain == normalized, cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException("Domain is already connected to another project.");
        }

        var project = await _db.UploadedProjects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(9)).ToLowerInvariant();

        var domain = new ProjectDomain
        {
            Id = Guid.NewGuid(),
            UploadedProjectId = project.Id,
            DomainName = normalized,
            NormalizedDomain = normalized,
            ApexRoot = DomainUtilities.GetApex(normalized),
            DomainType = ProjectDomainType.Custom,
            Status = DomainStatus.Pending,
            SslStatus = DomainSslStatus.Pending,
            VerificationToken = token,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        domain.DnsRecords.Add(new ProjectDomainDnsRecord
        {
            Id = Guid.NewGuid(),
            RecordType = "TXT",
            Name = $"_bugence-verify.{domain.DomainName}",
            Value = token,
            Purpose = "ownership",
            IsRequired = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        if (!string.IsNullOrWhiteSpace(_options.WildcardRecordTarget))
        {
            domain.DnsRecords.Add(new ProjectDomainDnsRecord
            {
                Id = Guid.NewGuid(),
                RecordType = "CNAME",
                Name = domain.DomainName,
                Value = _options.WildcardRecordTarget.Trim('.'),
                Purpose = "routing",
                IsRequired = true,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        if (!string.IsNullOrWhiteSpace(_options.EdgeIpAddress))
        {
            domain.DnsRecords.Add(new ProjectDomainDnsRecord
            {
                Id = Guid.NewGuid(),
                RecordType = "A",
                Name = domain.DomainName,
                Value = _options.EdgeIpAddress.Trim(),
                Purpose = "routing",
                IsRequired = string.IsNullOrWhiteSpace(_options.WildcardRecordTarget), // require A when no CNAME target
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        domain.DnsRecords.Add(new ProjectDomainDnsRecord
        {
            Id = Guid.NewGuid(),
            RecordType = "TXT",
            Name = $"_acme-challenge.{domain.DomainName}",
            Value = token,
            Purpose = "ssl",
            IsRequired = false,
            CreatedAtUtc = DateTime.UtcNow
        });

        _db.ProjectDomains.Add(domain);
        await _db.SaveChangesAsync(cancellationToken);
        return domain;
    }

    public async Task RemoveDomainAsync(int projectId, Guid domainId, CancellationToken cancellationToken = default)
    {
        var domain = await _db.ProjectDomains
            .FirstOrDefaultAsync(d => d.Id == domainId && d.UploadedProjectId == projectId, cancellationToken);

        if (domain is null)
        {
            return;
        }

        if (domain.DomainType == ProjectDomainType.Primary)
        {
            throw new InvalidOperationException("Primary domains cannot be removed.");
        }

        var project = await _db.UploadedProjects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        _db.ProjectDomains.Remove(domain);
        await _db.SaveChangesAsync(cancellationToken);
        _router.Evict(domain.DomainName);
        if (_options.PerProjectIisSites && project != null)
        {
            await _projectSites.RemoveDomainFromProjectSiteAsync(domain, project, cancellationToken);
            var remainingCustom = await _db.ProjectDomains
                .AsNoTracking()
                .CountAsync(d => d.UploadedProjectId == projectId && d.DomainType == ProjectDomainType.Custom, cancellationToken);
            if (remainingCustom == 0)
            {
                await _projectSites.DeleteProjectSiteIfUnusedAsync(projectId, cancellationToken);
            }
        }
        else
        {
            await _iisBindings.RemoveBindingsAsync(domain.DomainName, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProjectDomainDnsRecord>> GetDnsRecordsAsync(Guid domainId, CancellationToken cancellationToken = default)
    {
        return await _db.ProjectDomainDnsRecords
            .AsNoTracking()
            .Where(r => r.ProjectDomainId == domainId)
            .OrderBy(r => r.Purpose)
            .ThenBy(r => r.RecordType)
            .ToListAsync(cancellationToken);
    }

    private string BuildPrimaryDomain(string slug)
    {
        var zone = _options.PrimaryZone?.Trim('.') ?? "bugence.app";
        var cleanSlug = string.IsNullOrWhiteSpace(slug) ? "project" : slug.Trim('.').ToLowerInvariant();
        return $"{cleanSlug}.{zone}";
    }
}
