using BugenceEditConsole.Data;
using BugenceEditConsole.Infrastructure;
using BugenceEditConsole.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BugenceEditConsole.Services;

public record ProjectRoute(int ProjectId, string Slug, string? PublishStoragePath);

public interface IDomainRouter
{
    Task<ProjectRoute?> ResolveAsync(string host, CancellationToken cancellationToken = default);
    void Evict(string host);
}

public class DomainRouter : IDomainRouter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly DomainRoutingOptions _options;
    private readonly ILogger<DomainRouter> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public DomainRouter(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        IOptions<DomainRoutingOptions> options,
        ILogger<DomainRouter> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ProjectRoute?> ResolveAsync(string host, CancellationToken cancellationToken = default)
    {
        var normalized = DomainUtilities.Normalize(host);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (_cache.TryGetValue(normalized, out ProjectRoute cached))
        {
            return cached;
        }

        var route = await LoadRouteAsync(normalized, cancellationToken);
        if (route is not null)
        {
            _cache.Set(normalized, route, CacheDuration);
        }

        return route;
    }

    public void Evict(string host)
    {
        var normalized = DomainUtilities.Normalize(host);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            _cache.Remove(normalized);
        }
    }

    private async Task<ProjectRoute?> LoadRouteAsync(string normalizedHost, CancellationToken cancellationToken)
    {
        var zone = DomainUtilities.Normalize(_options.PrimaryZone);
        if (!string.IsNullOrWhiteSpace(zone) &&
            normalizedHost.EndsWith(zone, StringComparison.OrdinalIgnoreCase))
        {
            var prefix = normalizedHost[..^zone.Length].TrimEnd('.');
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                var slugRoute = await LoadProjectBySlugAsync(prefix, cancellationToken);
                if (slugRoute is not null)
                {
                    return slugRoute;
                }
            }
            else
            {
                _logger.LogDebug("Host {Host} matched primary zone but missing slug prefix.", normalizedHost);
            }
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var domain = await db.ProjectDomains
            .AsNoTracking()
            .Include(d => d.Project)
            .Where(d => d.Status == DomainStatus.Connected)
            .FirstOrDefaultAsync(d => d.NormalizedDomain == normalizedHost, cancellationToken);

        if (domain?.Project == null)
        {
            var alternateHost = GetWwwVariant(normalizedHost);
            if (!string.IsNullOrWhiteSpace(alternateHost))
            {
                domain = await db.ProjectDomains
                    .AsNoTracking()
                    .Include(d => d.Project)
                    .Where(d => d.Status == DomainStatus.Connected)
                    .FirstOrDefaultAsync(d => d.NormalizedDomain == alternateHost, cancellationToken);
            }
        }

        if (domain?.Project == null)
        {
            return null;
        }

        return new ProjectRoute(domain.Project.Id, domain.Project.Slug, domain.Project.PublishStoragePath);
    }

    private static string GetWwwVariant(string normalizedHost)
    {
        if (string.IsNullOrWhiteSpace(normalizedHost) || !normalizedHost.Contains('.', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (normalizedHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedHost["www.".Length..];
        }

        return "www." + normalizedHost;
    }

    private async Task<ProjectRoute?> LoadProjectBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var project = await db.UploadedProjects
            .AsNoTracking()
            .Where(p => p.Slug == slug)
            .Select(p => new { p.Id, p.Slug, p.PublishStoragePath })
            .FirstOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return null;
        }

        return new ProjectRoute(project.Id, project.Slug, project.PublishStoragePath);
    }
}
