namespace BugenceEditConsole.Models;

internal sealed record ContentHistoryEntryRecord(
    Guid Id,
    Guid SitePageId,
    Guid? PageSectionId,
    string FieldKey,
    string? PreviousValue,
    string? NewValue,
    string? ChangeSummary,
    string PerformedByUserId,
    string? PerformedByDisplayName,
    DateTime PerformedAtUtc);
