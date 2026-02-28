using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Support;

[Authorize]
public class DebugPanelModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DebugPanelLogService _debugLogService;

    public DebugPanelModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, DebugPanelLogService debugLogService)
    {
        _db = db;
        _userManager = userManager;
        _debugLogService = debugLogService;
    }

    public string UserName { get; private set; } = "Administrator";
    public string UserEmail { get; private set; } = "—";
    public string UserInitials { get; private set; } = "AD";
    public bool CanManage { get; private set; }
    public List<DebugErrorRow> Errors { get; private set; } = new();
    public string ErrorsJson { get; private set; } = "[]";

    public async Task<IActionResult> OnGetAsync()
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return RedirectToPage("/Auth/Login");
        }

        UserName = context.User.GetFriendlyName();
        if (string.IsNullOrWhiteSpace(UserName))
        {
            UserName = context.User.UserName ?? "Administrator";
        }

        UserEmail = string.IsNullOrWhiteSpace(context.User.Email) ? "—" : context.User.Email!;
        UserInitials = GetInitials(UserName);
        CanManage = context.CanManage;

        await _debugLogService.EnsureTableAsync(HttpContext.RequestAborted);
        Errors = await LoadErrorsAsync(context.OwnerUserId);
        ErrorsJson = JsonSerializer.Serialize(Errors, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        return Page();
    }

    private async Task<List<DebugErrorRow>> LoadErrorsAsync(string ownerUserId)
    {
        var list = new List<DebugErrorRow>();
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        await using var command = connection.CreateCommand();
        command.CommandText = isSqlite
            ? @"SELECT Id, Source, ShortDescription, LongDescription, Path, CreatedAtUtc, OwnerUserId
FROM DebugPanelErrors
WHERE (OwnerUserId = @owner OR OwnerUserId IS NULL)
ORDER BY CreatedAtUtc DESC
LIMIT 5000"
            : @"SELECT TOP 5000 Id, Source, ShortDescription, LongDescription, Path, CreatedAtUtc, OwnerUserId
FROM DebugPanelErrors
WHERE (OwnerUserId = @owner OR OwnerUserId IS NULL)
ORDER BY CreatedAtUtc DESC";
        AddParameter(command, "@owner", ownerUserId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new DebugErrorRow
            {
                Id = reader.GetInt32(0),
                Source = reader.IsDBNull(1) ? "Unknown" : reader.GetString(1),
                ShortDescription = reader.IsDBNull(2) ? "Unknown error" : reader.GetString(2),
                LongDescription = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Path = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                CreatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(5)) : reader.GetDateTime(5),
                OwnerUserId = reader.IsDBNull(6) ? null : reader.GetString(6)
            };

            if (list.Count > 0 && AreSameDisplayedError(list[^1], row))
            {
                list[^1].ErrorCount++;
                continue;
            }

            list.Add(row);
        }

        return list;
    }

    private static bool AreSameDisplayedError(DebugErrorRow current, DebugErrorRow next)
    {
        return string.Equals(current.Source, next.Source, StringComparison.Ordinal) &&
               string.Equals(current.ShortDescription, next.ShortDescription, StringComparison.Ordinal) &&
               string.Equals(current.LongDescription, next.LongDescription, StringComparison.Ordinal) &&
               string.Equals(current.Path, next.Path, StringComparison.Ordinal);
    }

    private async Task<AccessContext?> GetAccessContextAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return null;
        }

        var member = await _db.TeamMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == user.Id);

        var ownerUserId = member?.OwnerUserId ?? user.Id;
        var canManage = member == null || string.Equals(member.Role, "Admin", StringComparison.OrdinalIgnoreCase);

        return new AccessContext
        {
            User = user,
            OwnerUserId = ownerUserId,
            CanManage = canManage
        };
    }

    private static void AddParameter(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string GetInitials(string name)
    {
        var initials = new string(name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]))
            .Take(2)
            .ToArray());
        return string.IsNullOrWhiteSpace(initials) ? "AD" : initials;
    }

    private sealed class AccessContext
    {
        public required ApplicationUser User { get; init; }
        public required string OwnerUserId { get; init; }
        public bool CanManage { get; init; }
    }

    public class DebugErrorRow
    {
        public int Id { get; set; }
        public string Source { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string LongDescription { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public string? OwnerUserId { get; set; }
        public int ErrorCount { get; set; } = 1;
    }
}
