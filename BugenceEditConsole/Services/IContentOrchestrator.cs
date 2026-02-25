using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Http;

namespace BugenceEditConsole.Services;

public interface IContentOrchestrator
{
    Task<IReadOnlyList<SitePage>> GetPagesAsync(CancellationToken cancellationToken = default);
    Task<SitePage?> GetPageWithSectionsAsync(Guid pageId, CancellationToken cancellationToken = default);
    Task<PageSection?> GetSectionByIdAsync(Guid sectionId, CancellationToken cancellationToken = default);
    Task<PageSection?> GetSectionBySelectorAsync(Guid pageId, string selector, CancellationToken cancellationToken = default);
    Task<ContentChangeResult> UpdateSectionAsync(Guid sectionId, string? newContent, string? mediaAltText, IFormFile? image, string userId, string userDisplayName, CancellationToken cancellationToken = default);
    Task<ContentChangeResult> UpsertSectionAsync(Guid pageId, Guid? sectionId, string? selector, SectionContentType contentType, string? newContent, string? mediaAltText, IFormFile? image, string userId, string userDisplayName, CancellationToken cancellationToken = default);
    Task<ContentChangeResult> DeleteSectionAsync(Guid pageId, Guid sectionId, string userId, string userDisplayName, CancellationToken cancellationToken = default);
    Task SyncPageFromAssetAsync(Guid pageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContentChangeLog>> GetRecentLogsAsync(int take = 25, CancellationToken cancellationToken = default);
    Task<PublishResult> PublishPageAsync(Guid pageId, string userId, string userDisplayName, CancellationToken cancellationToken = default);
}

public record ContentChangeResult(bool Success, string? Message, PageSection? Section, ContentChangeLog? Log);
public record PublishResult(DateTime PublishedAtUtc, IReadOnlyList<string> Warnings);

