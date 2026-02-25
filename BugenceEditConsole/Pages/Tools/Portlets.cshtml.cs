using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Linq;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Tools;

[Authorize]
public class PortletsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DebugPanelLogService _debugLogService;

    public PortletsModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, DebugPanelLogService debugLogService)
    {
        _db = db;
        _userManager = userManager;
        _debugLogService = debugLogService;
    }

    public string UserName { get; private set; } = "Administrator";
    public string UserEmail { get; private set; } = "â€”";
    public string UserInitials { get; private set; } = "AD";
    public bool CanManage { get; private set; }
    public List<PortletRow> Portlets { get; private set; } = new();
    public string PortletsJson { get; private set; } = "[]";
    public List<PageOption> Pages { get; private set; } = new();
    public List<TemplateViewerOption> TemplateViewers { get; private set; } = new();
    public string PagesJson { get; private set; } = "[]";
    public string TemplateViewersJson { get; private set; } = "[]";

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

        UserEmail = string.IsNullOrWhiteSpace(context.User.Email) ? "â€”" : context.User.Email!;
        UserInitials = GetInitials(UserName);
        CanManage = context.CanManage;

        await EnsurePagesTableAsync();
        await EnsurePagePortletsTableAsync();
        await EnsureMasterpagesTableAsync();
        await EnsureTempleteViewerTableAsync();
        await EnsurePortletDguidAsync(context.OwnerUserId, context.CompanyId);
        Pages = await LoadPagesAsync(context.OwnerUserId, context.CompanyId);
        TemplateViewers = await LoadTemplateViewersAsync(context.OwnerUserId, context.CompanyId);
        Portlets = await LoadPortletsAsync(context.OwnerUserId, context.CompanyId);
        PortletsJson = JsonSerializer.Serialize(Portlets, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Default,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        PagesJson = JsonSerializer.Serialize(Pages, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Default,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        TemplateViewersJson = JsonSerializer.Serialize(TemplateViewers, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Default,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] PortletPayload payload)
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

            if (payload.PageId <= 0 || payload.TemplateViewerId <= 0 || string.IsNullOrWhiteSpace(payload.ZoneKey))
            {
                return new JsonResult(new { success = false, message = "Please provide page, zone, and template viewer." }) { StatusCode = 400 };
            }

            await EnsurePagePortletsTableAsync();
            await EnsurePortletDguidAsync(context.OwnerUserId, context.CompanyId);
            if (!await IsPageOwnerAsync(payload.PageId, context.OwnerUserId, context.CompanyId))
            {
                return new JsonResult(new { success = false, message = "Unauthorized page." }) { StatusCode = 403 };
            }
            if (!await TemplateViewerExistsAsync(payload.TemplateViewerId, context.OwnerUserId, context.CompanyId))
            {
                return new JsonResult(new { success = false, message = "Template viewer not found." }) { StatusCode = 404 };
            }

            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            var provider = _db.Database.ProviderName ?? string.Empty;
            var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
            var dguid = Guid.NewGuid();
            await using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO PagePortlets (OwnerUserId, CompanyId, PageId, DGUID, ZoneKey, TemplateViewerId, SortOrder, CreatedAtUtc, UpdatedAtUtc)
VALUES (@owner, @company, @page, @dguid, @zone, @viewer, @order, @created, @updated)";
            AddParameter(command, "@owner", context.OwnerUserId);
            AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
            AddParameter(command, "@page", payload.PageId);
            AddParameter(command, "@dguid", isSqlite ? dguid.ToString("N") : dguid);
            AddParameter(command, "@zone", payload.ZoneKey.Trim());
            AddParameter(command, "@viewer", payload.TemplateViewerId);
            AddParameter(command, "@order", payload.SortOrder);
            var now = DateTime.UtcNow;
            AddParameter(command, "@created", now);
            AddParameter(command, "@updated", now);
            var inserted = await command.ExecuteNonQueryAsync();
            if (inserted <= 0)
            {
                return new JsonResult(new { success = false, message = "Unable to create portlet." }) { StatusCode = 500 };
            }

            return new JsonResult(new { success = true, message = "Portlet created." });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("Portlets.Create", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Unable to create portlet." }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostUpdateAsync([FromBody] PortletUpdatePayload payload)
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

            if (payload.Id <= 0 || payload.PageId <= 0 || payload.TemplateViewerId <= 0 || string.IsNullOrWhiteSpace(payload.ZoneKey))
            {
                return new JsonResult(new { success = false, message = "Please provide page, zone, and template viewer." }) { StatusCode = 400 };
            }

            await EnsurePagePortletsTableAsync();
            await EnsurePortletDguidAsync(context.OwnerUserId, context.CompanyId);
            if (!await IsPageOwnerAsync(payload.PageId, context.OwnerUserId, context.CompanyId))
            {
                return new JsonResult(new { success = false, message = "Unauthorized page." }) { StatusCode = 403 };
            }
            if (!await TemplateViewerExistsAsync(payload.TemplateViewerId, context.OwnerUserId, context.CompanyId))
            {
                return new JsonResult(new { success = false, message = "Template viewer not found." }) { StatusCode = 404 };
            }

            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            var provider = _db.Database.ProviderName ?? string.Empty;
            var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
            await using var command = connection.CreateCommand();
            command.CommandText = @"UPDATE PagePortlets
	SET PageId = @page, ZoneKey = @zone, TemplateViewerId = @viewer, SortOrder = @order, UpdatedAtUtc = @updated
	WHERE Id = @id AND OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
            AddParameter(command, "@zone", payload.ZoneKey.Trim());
            AddParameter(command, "@viewer", payload.TemplateViewerId);
            AddParameter(command, "@order", payload.SortOrder);
            AddParameter(command, "@updated", DateTime.UtcNow);
            AddParameter(command, "@id", payload.Id);
            AddParameter(command, "@page", payload.PageId);
            AddParameter(command, "@owner", context.OwnerUserId);
            AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
            var updated = await command.ExecuteNonQueryAsync();
            if (updated == 0)
            {
                return new JsonResult(new { success = false, message = "Unable to update portlet." }) { StatusCode = 400 };
            }

            return new JsonResult(new { success = true, message = "Portlet updated." });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("Portlets.Update", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Unable to update portlet." }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync([FromBody] DeletePortletPayload payload)
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
                return new JsonResult(new { success = false, message = "Invalid portlet." }) { StatusCode = 400 };
            }

            await EnsurePagePortletsTableAsync();
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            var provider = _db.Database.ProviderName ?? string.Empty;
            var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM PagePortlets WHERE Id = @id AND OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
            AddParameter(command, "@id", payload.Id);
            AddParameter(command, "@owner", context.OwnerUserId);
            AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
            await command.ExecuteNonQueryAsync();

            return new JsonResult(new { success = true, message = "Portlet deleted." });
        }
        catch (Exception ex)
        {
            await _debugLogService.LogErrorAsync("Portlets.Delete", ex.Message, ex.ToString(), path: HttpContext.Request.Path, cancellationToken: HttpContext.RequestAborted);
            return new JsonResult(new { success = false, message = "Unable to delete portlet." }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostDatabaseSyncAsync()
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        await EnsurePagesTableAsync();
        await EnsurePagePortletsTableAsync();
        await EnsureMasterpagesTableAsync();
        await EnsureTempleteViewerTableAsync();
        await EnsurePortletDguidAsync(context.OwnerUserId, context.CompanyId);

        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var repaired = 0;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE PagePortlets SET CompanyId=@company, UpdatedAtUtc=@updated WHERE OwnerUserId=@owner AND CompanyId IS NULL";
            AddParameter(command, "@owner", context.OwnerUserId);
            AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
            AddParameter(command, "@updated", DateTime.UtcNow);
            repaired += await command.ExecuteNonQueryAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Pages SET CompanyId=@company, UpdatedAtUtc=@updated WHERE OwnerUserId=@owner AND CompanyId IS NULL";
            AddParameter(command, "@owner", context.OwnerUserId);
            AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
            AddParameter(command, "@updated", DateTime.UtcNow);
            repaired += await command.ExecuteNonQueryAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE TempleteViewers SET CompanyId=@company, UpdatedAtUtc=@updated WHERE OwnerUserId=@owner AND CompanyId IS NULL";
            AddParameter(command, "@owner", context.OwnerUserId);
            AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
            AddParameter(command, "@updated", DateTime.UtcNow);
            repaired += await command.ExecuteNonQueryAsync();
        }

        return new JsonResult(new { success = true, repaired, message = $"Database synced. {repaired} record(s) repaired." });
    }

    public Task<IActionResult> OnGetNextIdAsync() => OnGetNextIdentityAsync();

    public async Task<IActionResult> OnGetNextIdentityAsync()
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        await EnsurePagePortletsTableAsync();
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(Id) FROM PagePortlets WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
        AddParameter(command, "@owner", context.OwnerUserId);
        AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
        var result = await command.ExecuteScalarAsync();
        var nextId = result == null || result is DBNull ? 1 : Convert.ToInt32(result) + 1;
        return new JsonResult(new { success = true, nextId, dguid = Guid.NewGuid().ToString() });
    }

    private async Task<List<PortletRow>> LoadPortletsAsync(string ownerUserId, Guid? companyId)
    {
        var list = new List<PortletRow>();
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        await using var command = connection.CreateCommand();
        async Task RunQueryAsync(bool scoped)
        {
            command.Parameters.Clear();
            command.CommandText = scoped
                ? @"SELECT p.Id, p.CompanyId, p.DGUID, p.PageId, pg.Name, p.ZoneKey, p.TemplateViewerId, v.Name, p.SortOrder, p.CreatedAtUtc, p.UpdatedAtUtc
    FROM PagePortlets p
    LEFT JOIN Pages pg ON pg.Id = p.PageId AND ((pg.CompanyId = @company) OR (pg.CompanyId IS NULL AND @company IS NULL))
    LEFT JOIN TempleteViewers v ON v.Id = p.TemplateViewerId AND ((v.CompanyId = @company) OR (v.CompanyId IS NULL AND @company IS NULL))
    WHERE p.OwnerUserId = @owner AND ((p.CompanyId = @company) OR (p.CompanyId IS NULL AND @company IS NULL))
    ORDER BY p.CreatedAtUtc DESC"
                : @"SELECT p.Id, p.CompanyId, p.DGUID, p.PageId, pg.Name, p.ZoneKey, p.TemplateViewerId, v.Name, p.SortOrder, p.CreatedAtUtc, p.UpdatedAtUtc
    FROM PagePortlets p
    LEFT JOIN Pages pg ON pg.Id = p.PageId
    LEFT JOIN TempleteViewers v ON v.Id = p.TemplateViewerId
    ORDER BY p.CreatedAtUtc DESC";
            if (scoped)
            {
                AddParameter(command, "@owner", ownerUserId);
                AddCompanyParameter(command, "@company", companyId, isSqlite);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new PortletRow
                {
                    Id = reader.GetInt32(0),
                    CompanyId = reader.IsDBNull(1) ? string.Empty : (isSqlite ? reader.GetString(1) : reader.GetGuid(1).ToString()),
                    Dguid = isSqlite ? reader.GetString(2) : reader.GetGuid(2).ToString(),
                    PageId = reader.GetInt32(3),
                    PageName = reader.IsDBNull(4) ? "â€”" : reader.GetString(4),
                    ZoneKey = reader.GetString(5),
                    TemplateViewerId = reader.GetInt32(6),
                    TemplateViewerName = reader.IsDBNull(7) ? "â€”" : reader.GetString(7),
                    SortOrder = reader.GetInt32(8),
                    CreatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(9)) : reader.GetDateTime(9),
                    UpdatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(10)) : reader.GetDateTime(10)
                });
            }
        }

        await RunQueryAsync(scoped: true);
        if (list.Count == 0)
        {
            await RunQueryAsync(scoped: false);
        }

        return list;
    }

    private async Task<List<PageOption>> LoadPagesAsync(string ownerUserId, Guid? companyId)
    {
        var list = new List<PageOption>();
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        await using var command = connection.CreateCommand();
        async Task RunQueryAsync(bool scoped)
        {
            command.Parameters.Clear();
            command.CommandText = scoped
                ? @"SELECT p.Id, p.CompanyId, p.DGUID, p.Name, p.Slug, p.MasterpageId, m.Name, p.UpdatedAtUtc
    FROM Pages p
    LEFT JOIN Masterpages m ON m.Id = p.MasterpageId AND ((m.CompanyId = @company) OR (m.CompanyId IS NULL AND @company IS NULL))
    WHERE p.OwnerUserId = @owner AND ((p.CompanyId = @company) OR (p.CompanyId IS NULL AND @company IS NULL))
    ORDER BY p.Name"
                : @"SELECT p.Id, p.CompanyId, p.DGUID, p.Name, p.Slug, p.MasterpageId, m.Name, p.UpdatedAtUtc
    FROM Pages p
    LEFT JOIN Masterpages m ON m.Id = p.MasterpageId
    ORDER BY p.Name";
            if (scoped)
            {
                AddParameter(command, "@owner", ownerUserId);
                AddCompanyParameter(command, "@company", companyId, isSqlite);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new PageOption
                {
                    Id = reader.GetInt32(0),
                    CompanyId = reader.IsDBNull(1) ? string.Empty : (isSqlite ? reader.GetString(1) : reader.GetGuid(1).ToString()),
                    Dguid = reader.IsDBNull(2) ? string.Empty : (isSqlite ? reader.GetString(2) : reader.GetGuid(2).ToString()),
                    Name = reader.GetString(3),
                    Slug = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    MasterpageId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                    MasterpageName = reader.IsDBNull(6) ? "â€”" : reader.GetString(6),
                    UpdatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(7)) : reader.GetDateTime(7)
                });
            }
        }

        await RunQueryAsync(scoped: true);
        if (list.Count == 0)
        {
            await RunQueryAsync(scoped: false);
        }

        return list;
    }

    private async Task<List<TemplateViewerOption>> LoadTemplateViewersAsync(string ownerUserId, Guid? companyId)
    {
        var list = new List<TemplateViewerOption>();
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        await using var command = connection.CreateCommand();
        async Task RunQueryAsync(bool scoped)
        {
            command.Parameters.Clear();
            command.CommandText = scoped
                ? "SELECT Id, CompanyId, DGUID, Name, ViewerType, TemplateText, UpdatedAtUtc FROM TempleteViewers WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) ORDER BY Name"
                : "SELECT Id, CompanyId, DGUID, Name, ViewerType, TemplateText, UpdatedAtUtc FROM TempleteViewers ORDER BY Name";
            if (scoped)
            {
                AddParameter(command, "@owner", ownerUserId);
                AddCompanyParameter(command, "@company", companyId, isSqlite);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new TemplateViewerOption
                {
                    Id = reader.GetInt32(0),
                    CompanyId = reader.IsDBNull(1) ? string.Empty : (isSqlite ? reader.GetString(1) : reader.GetGuid(1).ToString()),
                    Dguid = reader.IsDBNull(2) ? string.Empty : (isSqlite ? reader.GetString(2) : reader.GetGuid(2).ToString()),
                    Name = reader.GetString(3),
                    ViewerType = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    TemplateText = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    UpdatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(6)) : reader.GetDateTime(6)
                });
            }
        }

        await RunQueryAsync(scoped: true);
        if (list.Count == 0)
        {
            await RunQueryAsync(scoped: false);
        }

        return list;
    }

    private async Task EnsurePagesTableAsync()
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _db.Database.ExecuteSqlRawAsync(@"
		CREATE TABLE IF NOT EXISTS Pages (
		    Id INTEGER PRIMARY KEY AUTOINCREMENT,
	    OwnerUserId TEXT NOT NULL,
	    CompanyId TEXT NULL,
	    DGUID TEXT NOT NULL,
    Name TEXT NOT NULL,
    Slug TEXT NOT NULL,
    MasterpageId INTEGER NULL,
    TemplateViewerId INTEGER NULL,
	    CreatedAtUtc TEXT NOT NULL,
	    UpdatedAtUtc TEXT NOT NULL
	);");
            using (var connection = _db.Database.GetDbConnection())
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }
                await using var pragma = connection.CreateCommand();
                pragma.CommandText = "PRAGMA table_info(\"Pages\")";
                await using var reader = await pragma.ExecuteReaderAsync();
                var hasCompanyId = false;
                while (await reader.ReadAsync())
                {
                    if (string.Equals(reader.GetString(1), "CompanyId", StringComparison.OrdinalIgnoreCase))
                    {
                        hasCompanyId = true;
                        break;
                    }
                }
                if (!hasCompanyId)
                {
                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE Pages ADD COLUMN CompanyId TEXT NULL;");
                }
            }
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Pages' AND xtype='U')
	CREATE TABLE Pages (
	    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
	    OwnerUserId NVARCHAR(450) NOT NULL,
	    CompanyId UNIQUEIDENTIFIER NULL,
	    DGUID UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    Slug NVARCHAR(200) NOT NULL,
    MasterpageId INT NULL,
    TemplateViewerId INT NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
	    UpdatedAtUtc DATETIME2 NOT NULL
	);");
        await _db.Database.ExecuteSqlRawAsync(@"
	IF EXISTS (SELECT * FROM sysobjects WHERE name='Pages' AND xtype='U')
	AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'CompanyId' AND Object_ID = Object_ID(N'Pages'))
	ALTER TABLE Pages ADD CompanyId UNIQUEIDENTIFIER NULL;
	");
    }

    private async Task EnsurePagePortletsTableAsync()
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _db.Database.ExecuteSqlRawAsync(@"
	CREATE TABLE IF NOT EXISTS PagePortlets (
	    Id INTEGER PRIMARY KEY AUTOINCREMENT,
	    OwnerUserId TEXT NOT NULL,
	    CompanyId TEXT NULL,
	    PageId INTEGER NOT NULL,
    DGUID TEXT NOT NULL,
    ZoneKey TEXT NOT NULL,
    TemplateViewerId INTEGER NOT NULL,
    SortOrder INTEGER NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);");
            using (var connection = _db.Database.GetDbConnection())
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }
                await using var pragma = connection.CreateCommand();
                pragma.CommandText = "PRAGMA table_info(\"PagePortlets\")";
                await using var reader = await pragma.ExecuteReaderAsync();
	                var hasDguid = false;
	                var hasCompanyId = false;
                while (await reader.ReadAsync())
                {
                    var colName = reader.GetString(1);
	                    if (string.Equals(colName, "DGUID", StringComparison.OrdinalIgnoreCase))
	                    {
	                        hasDguid = true;
	                    }
	                    if (string.Equals(colName, "CompanyId", StringComparison.OrdinalIgnoreCase))
	                    {
	                        hasCompanyId = true;
	                    }
	                }
	                if (!hasDguid)
	                {
	                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE PagePortlets ADD COLUMN DGUID TEXT;");
	                }
	                if (!hasCompanyId)
	                {
	                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE PagePortlets ADD COLUMN CompanyId TEXT NULL;");
	                }
	            }
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='PagePortlets' AND xtype='U')
	CREATE TABLE PagePortlets (
	    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
	    OwnerUserId NVARCHAR(450) NOT NULL,
	    CompanyId UNIQUEIDENTIFIER NULL,
	    PageId INT NOT NULL,
    DGUID UNIQUEIDENTIFIER NOT NULL,
    ZoneKey NVARCHAR(100) NOT NULL,
    TemplateViewerId INT NOT NULL,
    SortOrder INT NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL
);");
        await _db.Database.ExecuteSqlRawAsync(@"
	IF EXISTS (SELECT * FROM sysobjects WHERE name='PagePortlets' AND xtype='U')
	AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'DGUID' AND Object_ID = Object_ID(N'PagePortlets'))
	ALTER TABLE PagePortlets ADD DGUID UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
	");
        await _db.Database.ExecuteSqlRawAsync(@"
	IF EXISTS (SELECT * FROM sysobjects WHERE name='PagePortlets' AND xtype='U')
	AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'CompanyId' AND Object_ID = Object_ID(N'PagePortlets'))
	ALTER TABLE PagePortlets ADD CompanyId UNIQUEIDENTIFIER NULL;
	");
    }

    private async Task EnsureTempleteViewerTableAsync()
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _db.Database.ExecuteSqlRawAsync(@"
		CREATE TABLE IF NOT EXISTS TempleteViewers (
	    Id INTEGER PRIMARY KEY AUTOINCREMENT,
	    OwnerUserId TEXT NOT NULL,
	    CompanyId TEXT NULL,
	    DGUID TEXT NOT NULL,
    Name TEXT NOT NULL,
    ViewerType TEXT NOT NULL,
    TemplateText TEXT NOT NULL,
	    CreatedAtUtc TEXT NOT NULL,
	    UpdatedAtUtc TEXT NOT NULL
	);");
            using (var connection = _db.Database.GetDbConnection())
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }
                await using var pragma = connection.CreateCommand();
                pragma.CommandText = "PRAGMA table_info(\"TempleteViewers\")";
                await using var reader = await pragma.ExecuteReaderAsync();
                var hasCompanyId = false;
                while (await reader.ReadAsync())
                {
                    if (string.Equals(reader.GetString(1), "CompanyId", StringComparison.OrdinalIgnoreCase))
                    {
                        hasCompanyId = true;
                        break;
                    }
                }
                if (!hasCompanyId)
                {
                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE TempleteViewers ADD COLUMN CompanyId TEXT NULL;");
                }
            }
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='TempleteViewers' AND xtype='U')
	CREATE TABLE TempleteViewers (
	    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
	    OwnerUserId NVARCHAR(450) NOT NULL,
	    CompanyId UNIQUEIDENTIFIER NULL,
	    DGUID UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    ViewerType NVARCHAR(200) NOT NULL,
    TemplateText NVARCHAR(MAX) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
	    UpdatedAtUtc DATETIME2 NOT NULL
	);");
        await _db.Database.ExecuteSqlRawAsync(@"
	IF EXISTS (SELECT * FROM sysobjects WHERE name='TempleteViewers' AND xtype='U')
	AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'CompanyId' AND Object_ID = Object_ID(N'TempleteViewers'))
	ALTER TABLE TempleteViewers ADD CompanyId UNIQUEIDENTIFIER NULL;
	");
    }

    private async Task EnsureMasterpagesTableAsync()
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _db.Database.ExecuteSqlRawAsync(@"
		CREATE TABLE IF NOT EXISTS Masterpages (
	    Id INTEGER PRIMARY KEY AUTOINCREMENT,
	    OwnerUserId TEXT NOT NULL,
	    CompanyId TEXT NULL,
	    DGUID TEXT NOT NULL,
    Name TEXT NOT NULL,
    TemplateViewerId INTEGER NOT NULL,
    MasterpageText TEXT NOT NULL,
	    CreatedAtUtc TEXT NOT NULL,
	    UpdatedAtUtc TEXT NOT NULL
	);");
            using (var connection = _db.Database.GetDbConnection())
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }
                await using var pragma = connection.CreateCommand();
                pragma.CommandText = "PRAGMA table_info(\"Masterpages\")";
                await using var reader = await pragma.ExecuteReaderAsync();
                var hasCompanyId = false;
                while (await reader.ReadAsync())
                {
                    if (string.Equals(reader.GetString(1), "CompanyId", StringComparison.OrdinalIgnoreCase))
                    {
                        hasCompanyId = true;
                        break;
                    }
                }
                if (!hasCompanyId)
                {
                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE Masterpages ADD COLUMN CompanyId TEXT NULL;");
                }
            }
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Masterpages' AND xtype='U')
	CREATE TABLE Masterpages (
	    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
	    OwnerUserId NVARCHAR(450) NOT NULL,
	    CompanyId UNIQUEIDENTIFIER NULL,
	    DGUID UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    TemplateViewerId INT NOT NULL,
    MasterpageText NVARCHAR(MAX) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
	    UpdatedAtUtc DATETIME2 NOT NULL
	);");
        await _db.Database.ExecuteSqlRawAsync(@"
	IF EXISTS (SELECT * FROM sysobjects WHERE name='Masterpages' AND xtype='U')
	AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'CompanyId' AND Object_ID = Object_ID(N'Masterpages'))
	ALTER TABLE Masterpages ADD CompanyId UNIQUEIDENTIFIER NULL;
	");
    }

    private async Task<bool> IsPageOwnerAsync(int pageId, string ownerUserId, Guid? companyId)
    {
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM Pages WHERE Id = @id AND OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
        AddParameter(command, "@id", pageId);
        AddParameter(command, "@owner", ownerUserId);
        AddCompanyParameter(command, "@company", companyId, isSqlite);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private async Task<bool> TemplateViewerExistsAsync(int templateViewerId, string ownerUserId, Guid? companyId)
    {
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM TempleteViewers WHERE Id = @id AND OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
        AddParameter(command, "@id", templateViewerId);
        AddParameter(command, "@owner", ownerUserId);
        AddCompanyParameter(command, "@company", companyId, isSqlite);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
    }

    private async Task EnsurePortletDguidAsync(string ownerUserId, Guid? companyId)
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
            command.CommandText = "UPDATE PagePortlets SET DGUID = lower(hex(randomblob(16))) WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) AND (DGUID IS NULL OR DGUID = '')";
        }
        else
        {
            command.CommandText = "UPDATE PagePortlets SET DGUID = NEWID() WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) AND (DGUID IS NULL OR DGUID = '00000000-0000-0000-0000-000000000000')";
        }
        AddParameter(command, "@owner", ownerUserId);
        AddCompanyParameter(command, "@company", companyId, isSqlite);
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
        var canManage = member == null || string.Equals(member.Role, "Admin", StringComparison.OrdinalIgnoreCase);

        return new AccessContext
        {
            User = user,
            OwnerUserId = ownerUserId,
            CompanyId = user.CompanyId,
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

    private static void AddCompanyParameter(IDbCommand command, string name, Guid? companyId, bool isSqlite)
    {
        AddParameter(command, name, companyId.HasValue ? (isSqlite ? companyId.Value.ToString() : companyId.Value) : null);
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

    public class PortletRow
    {
        public int Id { get; set; }
        public string CompanyId { get; set; } = string.Empty;
        public string Dguid { get; set; } = string.Empty;
        public int PageId { get; set; }
        public string PageName { get; set; } = string.Empty;
        public string ZoneKey { get; set; } = string.Empty;
        public int TemplateViewerId { get; set; }
        public string TemplateViewerName { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    public class PageOption
    {
        public int Id { get; set; }
        public string CompanyId { get; set; } = string.Empty;
        public string Dguid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public int? MasterpageId { get; set; }
        public string MasterpageName { get; set; } = "â€”";
        public DateTime UpdatedAtUtc { get; set; }
    }

    public class TemplateViewerOption
    {
        public int Id { get; set; }
        public string CompanyId { get; set; } = string.Empty;
        public string Dguid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ViewerType { get; set; } = string.Empty;
        public string TemplateText { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; }
    }

    public class PortletPayload
    {
        public string? Dguid { get; set; }
        public int PageId { get; set; }
        public string ZoneKey { get; set; } = string.Empty;
        public int TemplateViewerId { get; set; }
        public int SortOrder { get; set; }
    }

    public class PortletUpdatePayload
    {
        public int Id { get; set; }
        public int PageId { get; set; }
        public string ZoneKey { get; set; } = string.Empty;
        public int TemplateViewerId { get; set; }
        public int SortOrder { get; set; }
    }

    public class DeletePortletPayload
    {
        public int Id { get; set; }
    }

    private static Guid ResolveDguid(string? value)
    {
        return Guid.TryParse(value, out var parsed) ? parsed : Guid.NewGuid();
    }
}



