using System.Globalization;
using System.Text;
using System.Text.Json;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Support;

public class ChangeLogModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public ChangeLogModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public sealed class ProjectOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class ActivityItem
    {
        public string EntryId { get; set; } = string.Empty;
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string Actor { get; set; } = "System";
        public string ActorInitials { get; set; } = "SY";
        public string TypeKey { get; set; } = "deploy";
        public string TypeLabel { get; set; } = "Deployment";
        public string CardTypeClass { get; set; } = "type-config";
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public DateTime AtUtc { get; set; }
        public string RelativeTime { get; set; } = string.Empty;
        public string LocalTimeText { get; set; } = string.Empty;
        public string SearchText { get; set; } = string.Empty;
        public IReadOnlyList<string> DetailLines { get; set; } = Array.Empty<string>();
        public string? DeployLogsUrl { get; set; }
    }

    public sealed class DateGroup
    {
        public string Label { get; set; } = string.Empty;
        public IReadOnlyList<ActivityItem> Entries { get; set; } = Array.Empty<ActivityItem>();
    }

    public IReadOnlyList<ProjectOption> Projects { get; private set; } = Array.Empty<ProjectOption>();
    public int SelectedProjectId { get; private set; }
    public string SelectedProjectName { get; private set; } = "Project";
    public IReadOnlyList<ActivityItem> Entries { get; private set; } = Array.Empty<ActivityItem>();
    public IReadOnlyList<DateGroup> Groups { get; private set; } = Array.Empty<DateGroup>();
    public IReadOnlyList<string> Members { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> EventTypes { get; private set; } = Array.Empty<string>();
    public int TotalEvents { get; private set; }
    public int Last24hEvents { get; private set; }
    public int DynamicEvents { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? projectId)
    {
        await LoadAsync(projectId, HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync(int? projectId, string? member, string? type, string? q)
    {
        await LoadAsync(projectId, HttpContext.RequestAborted);
        if (Entries.Count == 0)
        {
            return File(Encoding.UTF8.GetBytes("Project,Timestamp,Actor,Type,Title,Subtitle,Details\n"), "text/csv", "project-activity-empty.csv");
        }

        var filtered = ApplyFilters(Entries, member, type, q).ToList();
        var builder = new StringBuilder();
        builder.AppendLine("Project,Timestamp,Actor,Type,Title,Subtitle,Details");
        foreach (var entry in filtered)
        {
            var details = string.Join(" | ", entry.DetailLines.Take(10));
            builder
                .Append(Csv(entry.ProjectName)).Append(',')
                .Append(Csv(entry.AtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))).Append(',')
                .Append(Csv(entry.Actor)).Append(',')
                .Append(Csv(entry.TypeLabel)).Append(',')
                .Append(Csv(entry.Title)).Append(',')
                .Append(Csv(entry.Subtitle)).Append(',')
                .Append(Csv(details))
                .AppendLine();
        }

        var fileName = $"project-activity-{SelectedProjectId}-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv", fileName);
    }

    private async Task LoadAsync(int? projectId, CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        var scopedProjects = GetScopedProjectsQuery(user);
        var projectOptions = await scopedProjects
            .OrderByDescending(p => p.UploadedAtUtc)
            .Select(p => new ProjectOption
            {
                Id = p.Id,
                Name = string.IsNullOrWhiteSpace(p.DisplayName) ? p.FolderName : p.DisplayName!
            })
            .Take(100)
            .ToListAsync(cancellationToken);

        Projects = projectOptions;
        if (Projects.Count == 0)
        {
            SelectedProjectId = 0;
            SelectedProjectName = "No projects";
            Entries = Array.Empty<ActivityItem>();
            Groups = Array.Empty<DateGroup>();
            Members = Array.Empty<string>();
            EventTypes = Array.Empty<string>();
            return;
        }

        var projectExists = projectId.HasValue && Projects.Any(p => p.Id == projectId.Value);
        SelectedProjectId = projectExists ? projectId!.Value : Projects[0].Id;
        SelectedProjectName = Projects.First(p => p.Id == SelectedProjectId).Name;

        var loaded = await BuildEntriesAsync(SelectedProjectId, cancellationToken);
        Entries = loaded;
        Groups = loaded
            .GroupBy(e => e.AtUtc.ToLocalTime().Date)
            .OrderByDescending(g => g.Key)
            .Select(g => new DateGroup
            {
                Label = DateLabel(g.Key),
                Entries = g.ToList()
            })
            .ToList();

        Members = loaded
            .Select(e => e.Actor)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        EventTypes = loaded
            .Select(e => e.TypeKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var nowUtc = DateTime.UtcNow;
        TotalEvents = loaded.Count;
        Last24hEvents = loaded.Count(e => (nowUtc - e.AtUtc).TotalHours <= 24);
        DynamicEvents = loaded.Count(e => string.Equals(e.TypeKey, "dynamic", StringComparison.OrdinalIgnoreCase));
    }

    private IQueryable<UploadedProject> GetScopedProjectsQuery(ApplicationUser? user)
    {
        var query = _db.UploadedProjects.AsQueryable();
        if (user?.CompanyId != null)
        {
            return query.Where(p => p.CompanyId == user.CompanyId);
        }

        if (!string.IsNullOrWhiteSpace(user?.Id))
        {
            return query.Where(p => p.UserId == user.Id);
        }

        return query.Where(_ => false);
    }

    private async Task<List<ActivityItem>> BuildEntriesAsync(int projectId, CancellationToken cancellationToken)
    {
        const int take = 120;
        var project = await _db.UploadedProjects.FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);
        var projectName = project == null
            ? "Unknown Project"
            : (string.IsNullOrWhiteSpace(project.DisplayName) ? project.FolderName : project.DisplayName!);

        var deploys = await _db.PreviousDeploys
            .Where(d => d.UploadedProjectId == projectId)
            .OrderByDescending(d => d.StoredAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        var snapshots = await _db.ProjectDeploySnapshots
            .Where(s => s.UploadedProjectId == projectId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        var dynamicLogs = await _db.DynamicVeAuditLogs
            .Where(l => l.ProjectId == projectId)
            .OrderByDescending(l => l.AtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

        var actorUserIds = snapshots
            .Select(s => s.CreatedByUserId)
            .Concat(dynamicLogs.Select(l => l.ActorUserId))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToList();

        var usersById = await _db.Users
            .Where(u => actorUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => ResolveDisplayName(u), cancellationToken);

        var entries = new List<ActivityItem>(take * 3);

        foreach (var deploy in deploys)
        {
            entries.Add(MapDeploy(projectId, projectName, deploy));
        }

        foreach (var snapshot in snapshots)
        {
            entries.Add(MapSnapshot(projectId, projectName, snapshot, usersById));
        }

        foreach (var log in dynamicLogs)
        {
            entries.Add(MapDynamic(projectId, projectName, log, usersById));
        }

        return entries
            .OrderByDescending(e => e.AtUtc)
            .Take(take)
            .ToList();
    }

    private static ActivityItem MapDeploy(int projectId, string projectName, PreviousDeploy deploy)
    {
        var payload = ParsePayload(deploy.PayloadJson);
        var eventType = ReadString(payload, "eventType") ?? "deploy";
        var source = ReadString(payload, "source") ?? "system";
        var message = ReadString(payload, "message");
        var artifactSummary = BuildArtifactSummary(payload);
        var title = eventType switch
        {
            "publish" => "Published project",
            "reupload" => "Re-uploaded project files",
            "restore" => "Restored previous backup",
            "rollback" => "Rollback completed",
            _ => "Deployment event"
        };

        var subtitle = !string.IsNullOrWhiteSpace(message)
            ? message!
            : $"Source: {source}{(string.IsNullOrWhiteSpace(artifactSummary) ? string.Empty : $" • {artifactSummary}")}";

        var actor = SourceToActor(source);
        var atUtc = deploy.StoredAtUtc;
        return new ActivityItem
        {
            EntryId = $"deploy-{deploy.Id}",
            ProjectId = projectId,
            ProjectName = projectName,
            Actor = actor,
            ActorInitials = Initials(actor),
            TypeKey = "deploy",
            TypeLabel = "Deployment",
            CardTypeClass = "type-config",
            Title = title,
            Subtitle = subtitle,
            AtUtc = atUtc,
            LocalTimeText = atUtc.ToLocalTime().ToString("MMM d, h:mm tt"),
            RelativeTime = RelativeTimeFromNow(atUtc),
            DetailLines = BuildPayloadLines(deploy.PayloadJson),
            SearchText = $"{title} {subtitle} {actor} {eventType} {source}".ToLowerInvariant(),
            DeployLogsUrl = $"/DeployLogs/Project?projectId={projectId}"
        };
    }

    private static ActivityItem MapSnapshot(int projectId, string projectName, ProjectDeploySnapshot snapshot, IReadOnlyDictionary<string, string> usersById)
    {
        var actor = ResolveActor(snapshot.CreatedByUserId, usersById);
        var status = snapshot.IsSuccessful ? "successful" : "failed";
        var title = $"Snapshot created ({snapshot.Environment})";
        var subtitle = $"Source: {snapshot.Source} • {status}";
        var details = new List<string>
        {
            $"Snapshot ID: {snapshot.Id}",
            $"Environment: {snapshot.Environment}",
            $"Source: {snapshot.Source}",
            $"Root: {snapshot.RootPath}"
        };
        if (!string.IsNullOrWhiteSpace(snapshot.VersionLabel))
        {
            details.Add($"Version: {snapshot.VersionLabel}");
        }

        var atUtc = snapshot.CreatedAtUtc;
        return new ActivityItem
        {
            EntryId = $"snapshot-{snapshot.Id}",
            ProjectId = projectId,
            ProjectName = projectName,
            Actor = actor,
            ActorInitials = Initials(actor),
            TypeKey = "snapshot",
            TypeLabel = "Snapshot",
            CardTypeClass = "type-code",
            Title = title,
            Subtitle = subtitle,
            AtUtc = atUtc,
            LocalTimeText = atUtc.ToLocalTime().ToString("MMM d, h:mm tt"),
            RelativeTime = RelativeTimeFromNow(atUtc),
            DetailLines = details,
            SearchText = $"{title} {subtitle} {actor} {snapshot.Source} {snapshot.Environment}".ToLowerInvariant(),
            DeployLogsUrl = $"/DeployLogs/Project?projectId={projectId}"
        };
    }

    private static ActivityItem MapDynamic(int projectId, string projectName, DynamicVeAuditLog log, IReadOnlyDictionary<string, string> usersById)
    {
        var action = string.IsNullOrWhiteSpace(log.Action) ? "update" : log.Action.Trim().ToLowerInvariant();
        var actor = ResolveActor(log.ActorUserId, usersById);
        var payload = ParsePayload(log.PayloadJson);
        var pagePath = ReadString(payload, "pagePath");
        var elementKey = ReadString(payload, "elementKey");
        var title = action switch
        {
            "publish" => "Dynamic VE publish",
            "rollback" => "Dynamic VE rollback",
            "save-draft" => "Saved Dynamic VE draft",
            "text-set" => "Updated dynamic text",
            "style-set" => "Updated dynamic style",
            "section-insert" => "Inserted dynamic section",
            "action-bind" => "Bound dynamic action",
            _ => $"Dynamic VE {TitleCase(action.Replace("-", " "))}"
        };

        var focus = new[] { pagePath, elementKey }
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
        var subtitle = focus.Count > 0 ? string.Join(" • ", focus) : "Dynamic VE event recorded.";

        var atUtc = log.AtUtc;
        return new ActivityItem
        {
            EntryId = $"dve-{log.Id}",
            ProjectId = projectId,
            ProjectName = projectName,
            Actor = actor,
            ActorInitials = Initials(actor),
            TypeKey = "dynamic",
            TypeLabel = "Dynamic VE",
            CardTypeClass = "type-code",
            Title = title,
            Subtitle = subtitle,
            AtUtc = atUtc,
            LocalTimeText = atUtc.ToLocalTime().ToString("MMM d, h:mm tt"),
            RelativeTime = RelativeTimeFromNow(atUtc),
            DetailLines = BuildPayloadLines(log.PayloadJson),
            SearchText = $"{title} {subtitle} {actor} {action}".ToLowerInvariant(),
            DeployLogsUrl = null
        };
    }

    private static IEnumerable<ActivityItem> ApplyFilters(IEnumerable<ActivityItem> source, string? member, string? type, string? query)
    {
        var filtered = source;
        if (!string.IsNullOrWhiteSpace(member) && !string.Equals(member, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(e => string.Equals(e.Actor, member, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(type) && !string.Equals(type, "all", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(e => string.Equals(e.TypeKey, type, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLowerInvariant();
            filtered = filtered.Where(e => e.SearchText.Contains(term, StringComparison.Ordinal));
        }

        return filtered;
    }

    private static string ResolveDisplayName(ApplicationUser user)
    {
        var friendly = user.GetFriendlyName();
        if (!string.IsNullOrWhiteSpace(friendly))
        {
            return friendly;
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            return user.Email;
        }

        return "Administrator";
    }

    private static JsonElement? ParsePayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement? root, string property)
    {
        if (root is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.String)
        {
            return val.GetString();
        }

        return null;
    }

    private static string BuildArtifactSummary(JsonElement? root)
    {
        if (root is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (!element.TryGetProperty("artifact", out var artifact) || artifact.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var pieces = new List<string>();
        if (artifact.TryGetProperty("fileCount", out var fileCount) && fileCount.TryGetInt64(out var count))
        {
            pieces.Add($"{count} files");
        }
        if (artifact.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String)
        {
            pieces.Add(path.GetString() ?? string.Empty);
        }
        if (artifact.TryGetProperty("backup", out var backup) && backup.ValueKind == JsonValueKind.String)
        {
            pieces.Add($"backup: {backup.GetString()}");
        }

        return string.Join(" • ", pieces.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static IReadOnlyList<string> BuildPayloadLines(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            var lines = new List<string>();
            foreach (var prop in root.EnumerateObject())
            {
                var value = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Object => "{...}",
                    JsonValueKind.Array => $"[{prop.Value.GetArrayLength()} items]",
                    _ => prop.Value.GetRawText()
                };

                lines.Add($"{prop.Name}: {value}");
                if (lines.Count >= 12)
                {
                    break;
                }
            }

            return lines;
        }
        catch
        {
            return new[] { json.Length > 320 ? json[..320] : json };
        }
    }

    private static string ResolveActor(string? userId, IReadOnlyDictionary<string, string> usersById)
    {
        if (!string.IsNullOrWhiteSpace(userId) && usersById.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return "Administrator";
    }

    private static string SourceToActor(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "System";
        }

        var normalized = source.Trim().ToLowerInvariant();
        return normalized switch
        {
            "api" => "API",
            "system" => "System",
            "dashboard" => "Dashboard",
            "editor" => "Editor",
            _ => $"{TitleCase(normalized)}"
        };
    }

    private static string Csv(string value)
    {
        var safe = value.Replace("\"", "\"\"");
        return $"\"{safe}\"";
    }

    private static string Initials(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "SY";
        }

        var initials = new string(name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => char.ToUpperInvariant(x[0]))
            .Take(2)
            .ToArray());

        return string.IsNullOrWhiteSpace(initials) ? "SY" : initials;
    }

    private static string RelativeTimeFromNow(DateTime atUtc)
    {
        var span = DateTime.UtcNow - atUtc;
        if (span.TotalMinutes < 1)
        {
            return "just now";
        }
        if (span.TotalMinutes < 60)
        {
            return $"{(int)span.TotalMinutes}m ago";
        }
        if (span.TotalHours < 24)
        {
            return $"{(int)span.TotalHours}h ago";
        }
        if (span.TotalDays < 7)
        {
            return $"{(int)span.TotalDays}d ago";
        }
        return atUtc.ToLocalTime().ToString("MMM d");
    }

    private static string DateLabel(DateTime date)
    {
        var today = DateTime.Now.Date;
        if (date == today)
        {
            return "Today";
        }
        if (date == today.AddDays(-1))
        {
            return "Yesterday";
        }
        return date.ToString("MMM d, yyyy");
    }

    private static string TitleCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value);
    }
}
