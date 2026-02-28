using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BugenceEditConsole.Pages.Tools;

[Authorize]
public class SystemPropertiesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DebugPanelLogService _debugLogService;
    private readonly ISensitiveDataProtector _protector;
    private readonly IOptionsMonitorCache<OAuthOptions> _oauthOptionsCache;

    public SystemPropertiesModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        DebugPanelLogService debugLogService,
        ISensitiveDataProtector protector,
        IOptionsMonitorCache<OAuthOptions> oauthOptionsCache)
    {
        _db = db;
        _userManager = userManager;
        _debugLogService = debugLogService;
        _protector = protector;
        _oauthOptionsCache = oauthOptionsCache;
    }

    public string UserName { get; private set; } = "Administrator";
    public string UserEmail { get; private set; } = "-";
    public string UserInitials { get; private set; } = "AD";
    public bool CanManage { get; private set; }
    public List<SystemPropertyRow> Properties { get; private set; } = new();
    public string PropertiesJson { get; private set; } = "[]";

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

        UserEmail = string.IsNullOrWhiteSpace(context.User.Email) ? "-" : context.User.Email!;
        UserInitials = GetInitials(UserName);
        CanManage = context.CanManage;

        await EnsureTableAsync();
        await EnsureDguidValuesAsync(context.OwnerUserId, context.User.Id, context.CompanyId);
        Properties = await LoadPropertiesAsync(context.OwnerUserId, context.User.Id, context.CompanyId);
        for (var i = 0; i < Properties.Count; i++)
        {
            Properties[i].DisplayId = i + 1;
        }

        PropertiesJson = JsonSerializer.Serialize(Properties, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Default,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        return Page();
    }

    public async Task<IActionResult> OnGetNextIdentityAsync()
    {
        try
        {
            var context = await GetAccessContextAsync();
            if (context == null)
            {
                return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
            }

            await EnsureTableAsync();
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT MAX(Id) FROM SystemProperties WHERE (OwnerUserId = @owner OR OwnerUserId = @currentUser OR (@company IS NOT NULL AND CompanyId = @company) OR CompanyId IS NULL)";
            AddParameter(command, "@owner", context.OwnerUserId);
            AddParameter(command, "@currentUser", context.User.Id);
            AddCompanyParameter(command, context.CompanyId);
            var result = await command.ExecuteScalarAsync();
            var nextId = result == null || result is DBNull ? 1 : Convert.ToInt32(result) + 1;

            return new JsonResult(new
            {
                success = true,
                nextId,
                dguid = Guid.NewGuid().ToString()
            });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("SystemProperties.NextIdentity", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Unable to load next identity." }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] SystemPropertyPayload payload)
    {
        try
        {
            var context = await GetAccessContextAsync();
            if (context == null)
            {
                return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
            }

            if (!context.CanManage)
            {
                return new JsonResult(new { success = false, message = "You do not have permission to create records." }) { StatusCode = 403 };
            }

            var validation = ValidatePayload(payload);
            if (!validation.IsValid)
            {
                return new JsonResult(new { success = false, message = validation.Message }) { StatusCode = 400 };
            }

            await EnsureTableAsync();
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            var now = DateTime.UtcNow;
            var dguid = Guid.NewGuid();
            var provider = _db.Database.ProviderName ?? string.Empty;
            var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

            await using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO SystemProperties
(OwnerUserId, CompanyId, DGUID, Name, Category, Username, PasswordEncrypted, Host, Port, RouteUrl, Notes, CreatedAtUtc, UpdatedAtUtc)
VALUES (@owner, @company, @dguid, @name, @category, @username, @password, @host, @port, @route, @notes, @created, @updated)";
            AddParameter(command, "@owner", context.OwnerUserId);
            AddCompanyParameter(command, context.CompanyId);
            AddParameter(command, "@dguid", isSqlite ? dguid.ToString("N") : dguid);
            AddParameter(command, "@name", payload.Name.Trim());
            AddParameter(command, "@category", payload.Category.Trim());
            AddParameter(command, "@username", payload.Username?.Trim() ?? string.Empty);
            AddParameter(command, "@password", _protector.Protect(payload.Password?.Trim() ?? string.Empty));
            AddParameter(command, "@host", payload.Host?.Trim() ?? string.Empty);
            AddParameter(command, "@port", payload.Port?.Trim() ?? string.Empty);
            AddParameter(command, "@route", payload.RouteUrl?.Trim() ?? string.Empty);
            AddParameter(command, "@notes", payload.Notes?.Trim() ?? string.Empty);
            AddParameter(command, "@created", now);
            AddParameter(command, "@updated", now);
            var inserted = await command.ExecuteNonQueryAsync();
            if (inserted <= 0)
            {
                return new JsonResult(new { success = false, message = "Unable to create record." }) { StatusCode = 500 };
            }

            InvalidateOAuthCacheIfNeeded(payload.Category);
            return new JsonResult(new { success = true, message = "Record created." });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("SystemProperties.Create", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Unable to create record." }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostUpdateAsync([FromBody] SystemPropertyPayload payload)
    {
        try
        {
            var context = await GetAccessContextAsync();
            if (context == null)
            {
                return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
            }

            if (!context.CanManage)
            {
                return new JsonResult(new { success = false, message = "You do not have permission to update records." }) { StatusCode = 403 };
            }

            if (payload.Id <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid record." }) { StatusCode = 400 };
            }

            var validation = ValidatePayload(payload);
            if (!validation.IsValid)
            {
                return new JsonResult(new { success = false, message = validation.Message }) { StatusCode = 400 };
            }

            await EnsureTableAsync();
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText = @"UPDATE SystemProperties
SET Name = @name,
    Category = @category,
    Username = @username,
    PasswordEncrypted = @password,
    Host = @host,
    Port = @port,
    RouteUrl = @route,
    Notes = @notes,
    UpdatedAtUtc = @updated
WHERE Id = @id AND (OwnerUserId = @owner OR OwnerUserId = @currentUser OR (@company IS NOT NULL AND CompanyId = @company) OR CompanyId IS NULL)";
            AddParameter(command, "@name", payload.Name.Trim());
            AddParameter(command, "@category", payload.Category.Trim());
            AddParameter(command, "@username", payload.Username?.Trim() ?? string.Empty);
            AddParameter(command, "@password", _protector.Protect(payload.Password?.Trim() ?? string.Empty));
            AddParameter(command, "@host", payload.Host?.Trim() ?? string.Empty);
            AddParameter(command, "@port", payload.Port?.Trim() ?? string.Empty);
            AddParameter(command, "@route", payload.RouteUrl?.Trim() ?? string.Empty);
            AddParameter(command, "@notes", payload.Notes?.Trim() ?? string.Empty);
            AddParameter(command, "@updated", DateTime.UtcNow);
            AddParameter(command, "@id", payload.Id);
            AddParameter(command, "@owner", context.OwnerUserId);
            AddParameter(command, "@currentUser", context.User.Id);
            AddCompanyParameter(command, context.CompanyId);
            var updated = await command.ExecuteNonQueryAsync();
            if (updated <= 0)
            {
                return new JsonResult(new { success = false, message = "Record not found or not updated." }) { StatusCode = 404 };
            }

            InvalidateOAuthCacheIfNeeded(payload.Category);
            return new JsonResult(new { success = true, message = "Record updated." });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("SystemProperties.Update", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Unable to update record." }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync([FromBody] DeletePayload payload)
    {
        try
        {
            var context = await GetAccessContextAsync();
            if (context == null)
            {
                return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
            }

            if (!context.CanManage)
            {
                return new JsonResult(new { success = false, message = "You do not have permission to delete records." }) { StatusCode = 403 };
            }

            if (payload.Id <= 0)
            {
                return new JsonResult(new { success = false, message = "Invalid record." }) { StatusCode = 400 };
            }

            await EnsureTableAsync();
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM SystemProperties WHERE Id = @id AND (OwnerUserId = @owner OR OwnerUserId = @currentUser OR (@company IS NOT NULL AND CompanyId = @company) OR CompanyId IS NULL)";
            AddParameter(command, "@id", payload.Id);
            AddParameter(command, "@owner", context.OwnerUserId);
            AddParameter(command, "@currentUser", context.User.Id);
            AddCompanyParameter(command, context.CompanyId);
            await command.ExecuteNonQueryAsync();

            _oauthOptionsCache.TryRemove("Google");
            return new JsonResult(new { success = true, message = "Record deleted." });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("SystemProperties.Delete", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Unable to delete record." }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostDatabaseSyncAsync()
    {
        try
        {
            var context = await GetAccessContextAsync();
            if (context == null)
            {
                return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
            }

            await EnsureTableAsync();
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText = @"UPDATE SystemProperties
SET OwnerUserId = @owner,
    CompanyId = @company,
    UpdatedAtUtc = @updated
WHERE OwnerUserId = @owner OR OwnerUserId = @currentUser OR (@company IS NOT NULL AND CompanyId = @company) OR CompanyId IS NULL";
            AddParameter(command, "@owner", context.OwnerUserId);
            AddParameter(command, "@currentUser", context.User.Id);
            AddCompanyParameter(command, context.CompanyId);
            AddParameter(command, "@updated", DateTime.UtcNow);
            var repaired = await command.ExecuteNonQueryAsync();

            _oauthOptionsCache.TryRemove("Google");
            return new JsonResult(new { success = true, message = $"Database synced. {repaired} record(s) repaired." });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("SystemProperties.DatabaseSync", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Database sync failed." }) { StatusCode = 500 };
        }
    }

    private async Task<List<SystemPropertyRow>> LoadPropertiesAsync(string ownerUserId, string currentUserId, Guid? companyId)
    {
        var list = new List<SystemPropertyRow>();
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        await using var command = connection.CreateCommand();
command.CommandText = @"SELECT Id, CompanyId, DGUID, Name, Category, Username, PasswordEncrypted, Host, Port, RouteUrl, Notes, CreatedAtUtc, UpdatedAtUtc
FROM SystemProperties
WHERE (OwnerUserId = @owner OR OwnerUserId = @currentUser OR (@company IS NOT NULL AND CompanyId = @company) OR CompanyId IS NULL)
ORDER BY CreatedAtUtc DESC";
        AddParameter(command, "@owner", ownerUserId);
        AddParameter(command, "@currentUser", currentUserId);
        AddCompanyParameter(command, companyId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var encrypted = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
            list.Add(new SystemPropertyRow
            {
                Id = reader.GetInt32(0),
                CompanyId = reader.IsDBNull(1) ? string.Empty : (isSqlite ? reader.GetString(1) : reader.GetGuid(1).ToString()),
                Dguid = isSqlite ? reader.GetString(2) : reader.GetGuid(2).ToString(),
                Name = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Category = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Username = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                Password = SafeUnprotect(encrypted),
                Host = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                Port = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                RouteUrl = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                Notes = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                CreatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(11)) : reader.GetDateTime(11),
                UpdatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(12)) : reader.GetDateTime(12)
            });
        }

        return list;
    }

    private async Task EnsureTableAsync()
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS SystemProperties (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerUserId TEXT NOT NULL,
    CompanyId TEXT NULL,
    DGUID TEXT NOT NULL,
    Name TEXT NOT NULL,
    Category TEXT NOT NULL,
    Username TEXT NOT NULL,
    PasswordEncrypted TEXT NOT NULL,
    Host TEXT NOT NULL,
    Port TEXT NOT NULL,
    RouteUrl TEXT NOT NULL,
    Notes TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);");
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }
            await using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA table_info(\"SystemProperties\")";
            await using var reader = await pragma.ExecuteReaderAsync();
            var hasCompanyId = false;
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(1) && string.Equals(reader.GetString(1), "CompanyId", StringComparison.OrdinalIgnoreCase))
                {
                    hasCompanyId = true;
                    break;
                }
            }
            if (!hasCompanyId)
            {
                await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE SystemProperties ADD COLUMN CompanyId TEXT NULL;");
            }
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='SystemProperties' AND xtype='U')
CREATE TABLE SystemProperties (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NOT NULL,
    CompanyId UNIQUEIDENTIFIER NULL,
    DGUID UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    Category NVARCHAR(80) NOT NULL,
    Username NVARCHAR(200) NOT NULL,
    PasswordEncrypted NVARCHAR(MAX) NOT NULL,
    Host NVARCHAR(300) NOT NULL,
    Port NVARCHAR(20) NOT NULL,
    RouteUrl NVARCHAR(500) NOT NULL,
    Notes NVARCHAR(MAX) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL
);");
        await _db.Database.ExecuteSqlRawAsync(@"
IF EXISTS (SELECT * FROM sysobjects WHERE name='SystemProperties' AND xtype='U')
AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'CompanyId' AND Object_ID = Object_ID(N'SystemProperties'))
ALTER TABLE SystemProperties ADD CompanyId UNIQUEIDENTIFIER NULL;");
    }

    private async Task EnsureDguidValuesAsync(string ownerUserId, string currentUserId, Guid? companyId)
    {
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        if (isSqlite)
        {
            command.CommandText = "UPDATE SystemProperties SET DGUID = lower(hex(randomblob(16))) WHERE (OwnerUserId = @owner OR OwnerUserId = @currentUser OR (@company IS NOT NULL AND CompanyId = @company) OR CompanyId IS NULL) AND (DGUID IS NULL OR DGUID = '')";
        }
        else
        {
            command.CommandText = "UPDATE SystemProperties SET DGUID = NEWID() WHERE (OwnerUserId = @owner OR OwnerUserId = @currentUser OR (@company IS NOT NULL AND CompanyId = @company) OR CompanyId IS NULL) AND (DGUID IS NULL OR DGUID = '00000000-0000-0000-0000-000000000000')";
        }

        AddParameter(command, "@owner", ownerUserId);
        AddParameter(command, "@currentUser", currentUserId);
        AddCompanyParameter(command, companyId);
        await command.ExecuteNonQueryAsync();
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
        var ownerCompanyId = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == ownerUserId)
            .Select(u => u.CompanyId)
            .FirstOrDefaultAsync();
        var canManage = member == null || string.Equals(member.Role, "Admin", StringComparison.OrdinalIgnoreCase);

        return new AccessContext
        {
            User = user,
            OwnerUserId = ownerUserId,
            CompanyId = ownerCompanyId ?? user.CompanyId,
            CanManage = canManage
        };
    }

    private (bool IsValid, string Message) ValidatePayload(SystemPropertyPayload payload)
    {
        var name = payload.Name?.Trim() ?? string.Empty;
        var category = payload.Category?.Trim() ?? string.Empty;
        var username = payload.Username?.Trim() ?? string.Empty;
        var password = payload.Password?.Trim() ?? string.Empty;
        var routeUrl = payload.RouteUrl?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            return (false, "Name is required.");
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            return (false, "Category is required.");
        }

        var needsCredentials = category.Equals("SMTP", StringComparison.OrdinalIgnoreCase)
            || category.Equals("Credential", StringComparison.OrdinalIgnoreCase)
            || category.Equals("API", StringComparison.OrdinalIgnoreCase)
            || category.Equals("CertificateWebhook", StringComparison.OrdinalIgnoreCase)
            || category.Equals("OAuthGoogle", StringComparison.OrdinalIgnoreCase);
        var needsRoute = category.Equals("Route", StringComparison.OrdinalIgnoreCase)
            || category.Equals("CertificateWebhook", StringComparison.OrdinalIgnoreCase)
            || category.Equals("OAuthGoogle", StringComparison.OrdinalIgnoreCase);

        if (needsCredentials && !category.Equals("CertificateWebhook", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(username))
        {
            return category.Equals("OAuthGoogle", StringComparison.OrdinalIgnoreCase)
                ? (false, "Google Client ID is required for OAuth Google.")
                : (false, "Username is required for this category.");
        }

        if (needsCredentials && string.IsNullOrWhiteSpace(password))
        {
            return category.Equals("OAuthGoogle", StringComparison.OrdinalIgnoreCase)
                ? (false, "Google Client Secret is required for OAuth Google.")
                : category.Equals("CertificateWebhook", StringComparison.OrdinalIgnoreCase)
                    ? (false, "Webhook API key is required for CertificateWebhook.")
                : (false, "Password is required for this category.");
        }

        if (needsRoute && string.IsNullOrWhiteSpace(routeUrl))
        {
            return category.Equals("OAuthGoogle", StringComparison.OrdinalIgnoreCase)
                ? (false, "Redirect URI is required for OAuth Google.")
                : category.Equals("CertificateWebhook", StringComparison.OrdinalIgnoreCase)
                    ? (false, "Route URL is required for CertificateWebhook.")
                    : (false, "Route URL is required when category is Route.");
        }

        if (category.Equals("CertificateWebhook", StringComparison.OrdinalIgnoreCase))
        {
            var host = payload.Host?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(host))
            {
                return (false, "Host is required for CertificateWebhook.");
            }
        }

        if (category.Equals("OAuthGoogle", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(routeUrl, UriKind.Absolute, out var redirectUri))
            {
                return (false, "Redirect URI must be a full URL, e.g. http://localhost:5019/signin-google");
            }

            if (!string.Equals(redirectUri.AbsolutePath, "/signin-google", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Redirect URI path must be /signin-google for Google OAuth.");
            }
        }

        var port = payload.Port?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(port) && (!int.TryParse(port, out var parsedPort) || parsedPort <= 0 || parsedPort > 65535))
        {
            return (false, "Port must be a number between 1 and 65535.");
        }

        return (true, string.Empty);
    }

    private string SafeUnprotect(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return string.Empty;
        }

        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch
        {
            return string.Empty;
        }
    }

    private void InvalidateOAuthCacheIfNeeded(string? category)
    {
        if (string.Equals(category?.Trim(), "OAuthGoogle", StringComparison.OrdinalIgnoreCase))
        {
            _oauthOptionsCache.TryRemove("Google");
        }
    }

    private static void AddParameter(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private void AddCompanyParameter(IDbCommand command, Guid? companyId)
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        AddParameter(command, "@company", companyId.HasValue ? (isSqlite ? companyId.Value.ToString() : companyId.Value) : null);
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
        public Guid? CompanyId { get; init; }
        public bool CanManage { get; init; }
    }

    public class SystemPropertyRow
    {
        public int Id { get; set; }
        public int DisplayId { get; set; }
        public string CompanyId { get; set; } = string.Empty;
        public string Dguid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Port { get; set; } = string.Empty;
        public string RouteUrl { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    public class SystemPropertyPayload
    {
        public int Id { get; set; }
        public string Dguid { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Category { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Port { get; set; } = string.Empty;
        public string RouteUrl { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    public class DeletePayload
    {
        public int Id { get; set; }
    }
}


