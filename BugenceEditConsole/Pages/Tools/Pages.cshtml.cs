using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Linq;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Tools;

[Authorize]
public class PagesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public PagesModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public string UserName { get; private set; } = "Administrator";
    public string UserEmail { get; private set; } = "â€”";
    public string UserInitials { get; private set; } = "AD";
    public bool CanManage { get; private set; }
    public List<PageRow> Pages { get; private set; } = new();
    public string PagesJson { get; private set; } = "[]";
    public List<MasterpageOption> Masterpages { get; private set; } = new();
    public string MasterpagesJson { get; private set; } = "[]";
    public List<TemplateViewerOption> TemplateViewers { get; private set; } = new();

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

        await EnsureTableAsync();
        await EnsureDguidAsync(context.OwnerUserId, context.CompanyId);
        await EnsureSlugBackfillAsync(context.OwnerUserId, context.CompanyId);
        await EnsureMasterpagesTableAsync();
        await EnsureTempleteViewerTableAsync();
        Pages = await LoadPagesAsync(context.OwnerUserId, context.CompanyId);
        for (var i = 0; i < Pages.Count; i++)
        {
            Pages[i].DisplayId = i + 1;
        }
        PagesJson = JsonSerializer.Serialize(Pages, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Default,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        Masterpages = await LoadMasterpagesAsync(context.OwnerUserId, context.CompanyId);
        MasterpagesJson = JsonSerializer.Serialize(Masterpages, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Default,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        TemplateViewers = await LoadTemplateViewersAsync(context.OwnerUserId, context.CompanyId);

        return Page();
    }

    public Task<IActionResult> OnGetNextIdAsync() => OnGetNextIdentityAsync();

    public async Task<IActionResult> OnGetNextIdentityAsync()
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        await EnsureTableAsync();
        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        command.CommandText = "SELECT MAX(Id) FROM Pages WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
        AddParameter(command, "@owner", context.OwnerUserId);
        AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
        var result = await command.ExecuteScalarAsync();
        var nextId = result == null || result is DBNull ? 1 : Convert.ToInt32(result) + 1;
        return new JsonResult(new { success = true, nextId, dguid = Guid.NewGuid().ToString() });
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] PagePayload payload)
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

        if (string.IsNullOrWhiteSpace(payload.Name))
        {
            return new JsonResult(new { success = false, message = "Please provide page name." }) { StatusCode = 400 };
        }

        try
        {
            await EnsureTableAsync();
            await EnsureDguidAsync(context.OwnerUserId, context.CompanyId);
            await EnsureSlugBackfillAsync(context.OwnerUserId, context.CompanyId);
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            var now = DateTime.UtcNow;
            var provider = _db.Database.ProviderName ?? string.Empty;
            var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
            var dguidValue = Guid.NewGuid();
            var slugValue = await GetUniqueSlugAsync(context.OwnerUserId, context.CompanyId, payload.Slug, payload.Name, null);
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = @"INSERT INTO Pages (OwnerUserId, CompanyId, DGUID, Name, Slug, MasterpageId, CreatedAtUtc, UpdatedAtUtc)
VALUES (@owner, @company, @dguid, @name, @slug, @masterpage, @created, @updated)";
                AddParameter(command, "@owner", context.OwnerUserId);
                AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
                AddParameter(command, "@dguid", isSqlite ? dguidValue.ToString("N") : dguidValue);
                AddParameter(command, "@name", payload.Name.Trim());
                AddParameter(command, "@slug", slugValue);
                AddParameter(command, "@masterpage", payload.MasterpageId.HasValue ? payload.MasterpageId.Value : DBNull.Value);
                AddParameter(command, "@created", now);
                AddParameter(command, "@updated", now);
                var inserted = await command.ExecuteNonQueryAsync();
                if (inserted <= 0)
                {
                    return new JsonResult(new { success = false, message = "Unable to create record." }) { StatusCode = 500 };
                }
            }

            return new JsonResult(new { success = true, message = "Record created." });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = ex.Message }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostUpdateAsync([FromBody] PagePayload payload)
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

        if (payload.Id <= 0 || string.IsNullOrWhiteSpace(payload.Name))
        {
            return new JsonResult(new { success = false, message = "Please provide page name." }) { StatusCode = 400 };
        }

        try
        {
            await EnsureTableAsync();
            await EnsureDguidAsync(context.OwnerUserId, context.CompanyId);
            await EnsureSlugBackfillAsync(context.OwnerUserId, context.CompanyId);
            using var connection = _db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            var provider = _db.Database.ProviderName ?? string.Empty;
            var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
            var slugValue = await GetUniqueSlugAsync(context.OwnerUserId, context.CompanyId, payload.Slug, payload.Name, payload.Id);
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = @"UPDATE Pages
SET Name = @name, Slug = @slug, MasterpageId = @masterpage, UpdatedAtUtc = @updated
	WHERE Id = @id AND OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
                AddParameter(command, "@name", payload.Name.Trim());
                AddParameter(command, "@slug", slugValue);
                AddParameter(command, "@masterpage", payload.MasterpageId.HasValue ? payload.MasterpageId.Value : DBNull.Value);
                AddParameter(command, "@updated", DateTime.UtcNow);
                AddParameter(command, "@id", payload.Id);
                AddParameter(command, "@owner", context.OwnerUserId);
                AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
                var updated = await command.ExecuteNonQueryAsync();
                if (updated <= 0)
                {
                    return new JsonResult(new { success = false, message = "Record not found or not updated." }) { StatusCode = 404 };
                }
            }

            return new JsonResult(new { success = true, message = "Record updated." });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = ex.Message }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostDeleteAsync([FromBody] DeletePayload payload)
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

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM Pages WHERE Id = @id AND OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
            AddParameter(command, "@id", payload.Id);
            AddParameter(command, "@owner", context.OwnerUserId);
            AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
            await command.ExecuteNonQueryAsync();
        }

        return new JsonResult(new { success = true, message = "Record deleted." });
    }

    public async Task<IActionResult> OnPostDatabaseSyncAsync()
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        await EnsureTableAsync();
        await EnsureDguidAsync(context.OwnerUserId, context.CompanyId);
        await EnsureSlugBackfillAsync(context.OwnerUserId, context.CompanyId);
        await EnsureMasterpagesTableAsync();
        await EnsureTempleteViewerTableAsync();

        var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Pages SET CompanyId=@company, UpdatedAtUtc=@updated WHERE OwnerUserId=@owner AND CompanyId IS NULL";
        AddParameter(command, "@owner", context.OwnerUserId);
        AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
        AddParameter(command, "@updated", DateTime.UtcNow);
        var repaired = await command.ExecuteNonQueryAsync();

        return new JsonResult(new { success = true, repaired, message = $"Database synced. {repaired} record(s) repaired." });
    }

    public async Task<IActionResult> OnPostUpdateMasterpageAsync([FromBody] UpdateMasterpagePayload payload)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        if (!context.CanManage)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to update masterpages." }) { StatusCode = 403 };
        }

        if (payload.Id <= 0 || string.IsNullOrWhiteSpace(payload.Name) || payload.TemplateViewerId <= 0 || string.IsNullOrWhiteSpace(payload.MasterpageText))
        {
            return new JsonResult(new { success = false, message = "Please provide name, template viewer and masterpage content." }) { StatusCode = 400 };
        }

        await EnsureMasterpagesTableAsync();
        await EnsureTempleteViewerTableAsync();
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = @"UPDATE Masterpages
	SET Name = @name, TemplateViewerId = @viewer, MasterpageText = @text, UpdatedAtUtc = @updated
	WHERE Id = @id AND OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
        AddParameter(command, "@name", payload.Name.Trim());
        AddParameter(command, "@viewer", payload.TemplateViewerId);
        AddParameter(command, "@text", payload.MasterpageText.Trim());
        AddParameter(command, "@updated", DateTime.UtcNow);
        AddParameter(command, "@id", payload.Id);
        AddParameter(command, "@owner", context.OwnerUserId);
        AddCompanyParameter(command, "@company", context.CompanyId, isSqlite);
        var updated = await command.ExecuteNonQueryAsync();
        if (updated == 0)
        {
            return new JsonResult(new { success = false, message = "Masterpage not found or unauthorized." }) { StatusCode = 404 };
        }

        return new JsonResult(new { success = true, message = "Masterpage updated." });
    }

    private async Task<List<PageRow>> LoadPagesAsync(string ownerUserId, Guid? companyId)
    {
        var list = new List<PageRow>();
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
                ? @"SELECT p.Id, p.CompanyId, p.DGUID, p.Name, p.Slug, p.MasterpageId, m.Name, p.CreatedAtUtc, p.UpdatedAtUtc
    FROM Pages p
    LEFT JOIN Masterpages m ON m.Id = p.MasterpageId AND ((m.CompanyId = @company) OR (m.CompanyId IS NULL AND @company IS NULL))
    WHERE p.OwnerUserId = @owner AND ((p.CompanyId = @company) OR (p.CompanyId IS NULL AND @company IS NULL))
    ORDER BY p.CreatedAtUtc DESC"
                : @"SELECT p.Id, p.CompanyId, p.DGUID, p.Name, p.Slug, p.MasterpageId, m.Name, p.CreatedAtUtc, p.UpdatedAtUtc
    FROM Pages p
    LEFT JOIN Masterpages m ON m.Id = p.MasterpageId
    ORDER BY p.CreatedAtUtc DESC";
            if (scoped)
            {
                AddParameter(command, "@owner", ownerUserId);
                AddCompanyParameter(command, "@company", companyId, isSqlite);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new PageRow
                {
                    Id = reader.GetInt32(0),
                    CompanyId = reader.IsDBNull(1) ? string.Empty : (isSqlite ? reader.GetString(1) : reader.GetGuid(1).ToString()),
                    Dguid = isSqlite ? reader.GetString(2) : reader.GetGuid(2).ToString(),
                    Name = reader.GetString(3),
                    Slug = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    MasterpageId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                    MasterpageName = reader.IsDBNull(6) ? "â€”" : reader.GetString(6),
                    CreatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(7)) : reader.GetDateTime(7),
                    UpdatedAtUtc = isSqlite ? DateTime.Parse(reader.GetString(8)) : reader.GetDateTime(8)
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

    private async Task EnsureTableAsync()
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
	                var hasDguid = false;
	                var hasSlug = false;
	                var hasCompanyId = false;
                while (await reader.ReadAsync())
                {
                    var colName = reader.GetString(1);
                    if (string.Equals(colName, "DGUID", StringComparison.OrdinalIgnoreCase))
                    {
                        hasDguid = true;
                    }
	                    if (string.Equals(colName, "Slug", StringComparison.OrdinalIgnoreCase))
	                    {
	                        hasSlug = true;
	                    }
	                    if (string.Equals(colName, "CompanyId", StringComparison.OrdinalIgnoreCase))
	                    {
	                        hasCompanyId = true;
	                    }
	                }
                if (!hasDguid)
                {
                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE Pages ADD COLUMN DGUID TEXT;");
                }
	                if (!hasSlug)
	                {
	                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE Pages ADD COLUMN Slug TEXT NOT NULL DEFAULT '';");
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
AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'DGUID' AND Object_ID = Object_ID(N'Pages'))
ALTER TABLE Pages ADD DGUID UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
");
        await _db.Database.ExecuteSqlRawAsync(@"
	IF EXISTS (SELECT * FROM sysobjects WHERE name='Pages' AND xtype='U')
	AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'Slug' AND Object_ID = Object_ID(N'Pages'))
	ALTER TABLE Pages ADD Slug NVARCHAR(200) NOT NULL DEFAULT('');
	");
        await _db.Database.ExecuteSqlRawAsync(@"
	IF EXISTS (SELECT * FROM sysobjects WHERE name='Pages' AND xtype='U')
	AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'CompanyId' AND Object_ID = Object_ID(N'Pages'))
	ALTER TABLE Pages ADD CompanyId UNIQUEIDENTIFIER NULL;
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

    private async Task<List<MasterpageOption>> LoadMasterpagesAsync(string ownerUserId, Guid? companyId)
    {
        var list = new List<MasterpageOption>();
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
                ? "SELECT Id, Name, TemplateViewerId, MasterpageText FROM Masterpages WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) ORDER BY Name"
                : "SELECT Id, Name, TemplateViewerId, MasterpageText FROM Masterpages ORDER BY Name";
            if (scoped)
            {
                AddParameter(command, "@owner", ownerUserId);
                AddCompanyParameter(command, "@company", companyId, isSqlite);
            }

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new MasterpageOption
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    TemplateViewerId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    MasterpageText = reader.IsDBNull(3) ? string.Empty : reader.GetString(3)
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
                ? "SELECT Id, Name FROM TempleteViewers WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) ORDER BY Name"
                : "SELECT Id, Name FROM TempleteViewers ORDER BY Name";
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
                    Name = reader.GetString(1)
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

    private async Task EnsureDguidAsync(string ownerUserId, Guid? companyId)
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
            command.CommandText = "UPDATE Pages SET DGUID = lower(hex(randomblob(16))) WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) AND (DGUID IS NULL OR DGUID = '')";
        }
        else
        {
            command.CommandText = "UPDATE Pages SET DGUID = NEWID() WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) AND (DGUID IS NULL OR DGUID = '00000000-0000-0000-0000-000000000000')";
        }
        AddParameter(command, "@owner", ownerUserId);
        AddCompanyParameter(command, "@company", companyId, isSqlite);
        await command.ExecuteNonQueryAsync();
    }

    private async Task EnsureSlugBackfillAsync(string ownerUserId, Guid? companyId)
    {
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var rows = new List<(int Id, string Name)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, Name FROM Pages WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) AND (Slug IS NULL OR Slug = '')";
            AddParameter(command, "@owner", ownerUserId);
            AddCompanyParameter(command, "@company", companyId, isSqlite);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetInt32(0), reader.GetString(1)));
            }
        }

        foreach (var row in rows)
        {
            var slug = await GetUniqueSlugAsync(ownerUserId, companyId, null, row.Name, row.Id);
            await using var update = connection.CreateCommand();
            update.CommandText = "UPDATE Pages SET Slug = @slug WHERE Id = @id AND OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
            AddParameter(update, "@slug", slug);
            AddParameter(update, "@id", row.Id);
            AddParameter(update, "@owner", ownerUserId);
            AddCompanyParameter(update, "@company", companyId, isSqlite);
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }
            await update.ExecuteNonQueryAsync();
        }
    }

    private async Task<string> GetUniqueSlugAsync(string ownerUserId, Guid? companyId, string? requestedSlug, string nameFallback, int? excludeId)
    {
        var baseSlug = Slugify(string.IsNullOrWhiteSpace(requestedSlug) ? nameFallback : requestedSlug);
        if (string.IsNullOrWhiteSpace(baseSlug))
        {
            baseSlug = "page";
        }

        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var suffix = 0;
        while (true)
        {
            var candidate = suffix == 0 ? baseSlug : $"{baseSlug}-{suffix}";
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM Pages WHERE OwnerUserId = @owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) AND Slug = @slug" + (excludeId.HasValue ? " AND Id <> @id" : string.Empty);
            AddParameter(command, "@owner", ownerUserId);
            AddCompanyParameter(command, "@company", companyId, isSqlite);
            AddParameter(command, "@slug", candidate);
            if (excludeId.HasValue)
            {
                AddParameter(command, "@id", excludeId.Value);
            }
            var exists = Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
            if (!exists)
            {
                return candidate;
            }
            suffix++;
        }
    }

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();
        var buffer = new System.Text.StringBuilder();
        var prevDash = false;
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
            {
                buffer.Append(ch);
                prevDash = false;
                continue;
            }
            if (!prevDash)
            {
                buffer.Append('-');
                prevDash = true;
            }
        }
        var slug = buffer.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? string.Empty : slug;
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

    public class PageRow
    {
        public int Id { get; set; }
        public int DisplayId { get; set; }
        public string CompanyId { get; set; } = string.Empty;
        public string Dguid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public int? MasterpageId { get; set; }
        public string MasterpageName { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    public class PagePayload
    {
        public int Id { get; set; }
        public string? Dguid { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        public string? Slug { get; set; }
        public int? MasterpageId { get; set; }
    }

    public class MasterpageOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TemplateViewerId { get; set; }
        public string MasterpageText { get; set; } = string.Empty;
    }

    public class TemplateViewerOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class UpdateMasterpagePayload
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public int TemplateViewerId { get; set; }

        [Required]
        public string MasterpageText { get; set; } = string.Empty;
    }

    public class DeletePayload
    {
        public int Id { get; set; }
    }

    private static Guid ResolveDguid(string? value)
    {
        return Guid.TryParse(value, out var parsed) ? parsed : Guid.NewGuid();
    }
}



