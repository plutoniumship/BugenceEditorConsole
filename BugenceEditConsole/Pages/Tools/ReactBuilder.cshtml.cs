using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Common;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Tools;

[Authorize]
public class ReactBuilderModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public ReactBuilderModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public string UserName { get; private set; } = "Administrator";
    public string UserEmail { get; private set; } = "-";
    public string UserInitials { get; private set; } = "AD";
    public bool CanManage { get; private set; }
    public List<ReactBuilderRow> ReactBuilders { get; private set; } = new();
    public string ReactBuildersJson { get; private set; } = "[]";
    public List<PageOptionRow> Pages { get; private set; } = new();
    public string PagesJson { get; private set; } = "[]";

    public async Task<IActionResult> OnGetAsync()
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return RedirectToPage("/Auth/Login");

        UserName = string.IsNullOrWhiteSpace(ctx.User.GetFriendlyName()) ? (ctx.User.UserName ?? "Administrator") : ctx.User.GetFriendlyName();
        UserEmail = string.IsNullOrWhiteSpace(ctx.User.Email) ? "-" : ctx.User.Email!;
        UserInitials = GetInitials(UserName);
        CanManage = ctx.CanManage;

        await EnsureReactBuilderTablesAsync();
        await EnsurePagesTableAsync();

        ReactBuilders = await LoadReactBuildersAsync(ctx.OwnerUserId, ctx.CompanyId);
        if (ReactBuilders.Count == 0 && ctx.CompanyId.HasValue)
        {
            await RepairLegacyCompanyScopeAsync(ctx);
            ReactBuilders = await LoadReactBuildersAsync(ctx.OwnerUserId, ctx.CompanyId);
        }
        for (var i = 0; i < ReactBuilders.Count; i++) ReactBuilders[i].DisplayId = i + 1;
        Pages = await LoadPagesAsync(ctx.OwnerUserId, ctx.CompanyId);

        ReactBuildersJson = JsonSerializer.Serialize(ReactBuilders, JsonOpts());
        PagesJson = JsonSerializer.Serialize(Pages, JsonOpts());
        return Page();
    }

    public async Task<IActionResult> OnGetNextIdentityAsync()
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return UnauthorizedJson();
        await EnsureReactBuilderTablesAsync();

        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();

        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT MAX(Id) FROM ReactBuilders WHERE OwnerUserId=@owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
        Add(cmd, "@owner", ctx.OwnerUserId);
        AddCompany(cmd, "@company", ctx.CompanyId, isSqlite);
        var max = await cmd.ExecuteScalarAsync();
        var nextId = max == null || max is DBNull ? 1 : Convert.ToInt32(max) + 1;
        return new JsonResult(new { success = true, nextId, dguid = Guid.NewGuid().ToString() });
    }

    public async Task<IActionResult> OnGetPagesAsync()
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return UnauthorizedJson();
        await EnsurePagesTableAsync();
        return new JsonResult(new { success = true, pages = await LoadPagesAsync(ctx.OwnerUserId, ctx.CompanyId) });
    }

    public async Task<IActionResult> OnGetConnectionsAsync(int pageId)
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return UnauthorizedJson();
        if (pageId <= 0) return BadJson("Invalid page id.");

        await EnsureReactBuilderTablesAsync();
        await EnsurePagesTableAsync();
        if (!await PageInScopeAsync(pageId, ctx.OwnerUserId, ctx.CompanyId)) return NotFoundJson("Page not found.");

        var row = await LoadConnectionAsync(ctx.OwnerUserId, ctx.CompanyId, pageId);
        object connectionPayload = row == null
            ? new { pageId, topReactBuilderId = (int?)null, menuReactBuilderId = (int?)null, bodyReactBuilderId = (int?)null, subtemplateReactBuilderId = (int?)null }
            : new { pageId = row.PageId, topReactBuilderId = row.TopReactBuilderId, menuReactBuilderId = row.MenuReactBuilderId, bodyReactBuilderId = row.BodyReactBuilderId, subtemplateReactBuilderId = row.SubtemplateReactBuilderId };
        return new JsonResult(new
        {
            success = true,
            connection = connectionPayload
        });
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] ReactBuilderPayload payload)
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return UnauthorizedJson();
        if (!ctx.CanManage) return ForbiddenJson("You do not have permission to create records.");

        var name = payload.Name?.Trim() ?? string.Empty;
        var entry = NormalizeEntryPath(payload.EntryFilePath);
        if (string.IsNullOrWhiteSpace(name)) return BadJson("Please provide builder name.");
        if (string.IsNullOrWhiteSpace(entry)) return BadJson("Invalid entry file path.");

        await EnsureReactBuilderTablesAsync();
        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();
        var now = DateTime.UtcNow;

        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"INSERT INTO ReactBuilders (OwnerUserId, CompanyId, DGUID, Name, Description, EntryFilePath, PreviewMasterpageId, IsActive, CreatedAtUtc, UpdatedAtUtc)
VALUES (@owner, @company, @dguid, @name, @desc, @entry, @preview, @active, @created, @updated)";
            Add(cmd, "@owner", ctx.OwnerUserId);
            AddCompany(cmd, "@company", ctx.CompanyId, isSqlite);
            Add(cmd, "@dguid", isSqlite ? Guid.NewGuid().ToString("N") : Guid.NewGuid());
            Add(cmd, "@name", name);
            Add(cmd, "@desc", string.IsNullOrWhiteSpace(payload.Description) ? DBNull.Value : payload.Description.Trim());
            Add(cmd, "@entry", entry);
            Add(cmd, "@preview", payload.PreviewMasterpageId.HasValue ? payload.PreviewMasterpageId.Value : DBNull.Value);
            Add(cmd, "@active", payload.IsActive.GetValueOrDefault(true));
            Add(cmd, "@created", now);
            Add(cmd, "@updated", now);
            await cmd.ExecuteNonQueryAsync();
        }

        var id = await LastInsertIdAsync(c, isSqlite);
        await InsertDefaultWorkspaceAsync(c, isSqlite, ctx, id, now);
        return new JsonResult(new { success = true, id, message = "React builder created." });
    }

    public async Task<IActionResult> OnPostUpdateAsync([FromBody] ReactBuilderPayload payload)
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return UnauthorizedJson();
        if (!ctx.CanManage) return ForbiddenJson("You do not have permission to update records.");
        if (payload.Id <= 0) return BadJson("Invalid record.");

        var name = payload.Name?.Trim() ?? string.Empty;
        var entry = NormalizeEntryPath(payload.EntryFilePath);
        if (string.IsNullOrWhiteSpace(name)) return BadJson("Please provide builder name.");
        if (string.IsNullOrWhiteSpace(entry)) return BadJson("Invalid entry file path.");

        await EnsureReactBuilderTablesAsync();
        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();

        await using var cmd = c.CreateCommand();
        cmd.CommandText = @"UPDATE ReactBuilders
SET Name=@name, Description=@desc, EntryFilePath=@entry, PreviewMasterpageId=@preview, IsActive=@active, UpdatedAtUtc=@updated
WHERE Id=@id AND OwnerUserId=@owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
        Add(cmd, "@name", name);
        Add(cmd, "@desc", string.IsNullOrWhiteSpace(payload.Description) ? DBNull.Value : payload.Description.Trim());
        Add(cmd, "@entry", entry);
        Add(cmd, "@preview", payload.PreviewMasterpageId.HasValue ? payload.PreviewMasterpageId.Value : DBNull.Value);
        Add(cmd, "@active", payload.IsActive.GetValueOrDefault(true));
        Add(cmd, "@updated", DateTime.UtcNow);
        Add(cmd, "@id", payload.Id);
        Add(cmd, "@owner", ctx.OwnerUserId);
        AddCompany(cmd, "@company", ctx.CompanyId, isSqlite);
        var rows = await cmd.ExecuteNonQueryAsync();
        if (rows <= 0) return NotFoundJson("Record not found.");

        return new JsonResult(new { success = true, message = "React builder updated." });
    }

    public async Task<IActionResult> OnPostDeleteAsync([FromBody] DeletePayload payload)
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return UnauthorizedJson();
        if (!ctx.CanManage) return ForbiddenJson("You do not have permission to delete records.");
        if (payload.Id <= 0) return BadJson("Invalid record.");

        await EnsureReactBuilderTablesAsync();
        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();

        await using (var clear = c.CreateCommand())
        {
            clear.CommandText = @"UPDATE ReactPageConnections
SET TopReactBuilderId=CASE WHEN TopReactBuilderId=@id THEN NULL ELSE TopReactBuilderId END,
    MenuReactBuilderId=CASE WHEN MenuReactBuilderId=@id THEN NULL ELSE MenuReactBuilderId END,
    BodyReactBuilderId=CASE WHEN BodyReactBuilderId=@id THEN NULL ELSE BodyReactBuilderId END,
    SubtemplateReactBuilderId=CASE WHEN SubtemplateReactBuilderId=@id THEN NULL ELSE SubtemplateReactBuilderId END,
    UpdatedAtUtc=@updated
WHERE OwnerUserId=@owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)
AND (TopReactBuilderId=@id OR MenuReactBuilderId=@id OR BodyReactBuilderId=@id OR SubtemplateReactBuilderId=@id)";
            Add(clear, "@id", payload.Id);
            Add(clear, "@updated", DateTime.UtcNow);
            Add(clear, "@owner", ctx.OwnerUserId);
            AddCompany(clear, "@company", ctx.CompanyId, isSqlite);
            await clear.ExecuteNonQueryAsync();
        }

        await using (var delFiles = c.CreateCommand())
        {
            delFiles.CommandText = "DELETE FROM ReactBuilderFiles WHERE ReactBuilderId=@id AND OwnerUserId=@owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
            Add(delFiles, "@id", payload.Id);
            Add(delFiles, "@owner", ctx.OwnerUserId);
            AddCompany(delFiles, "@company", ctx.CompanyId, isSqlite);
            await delFiles.ExecuteNonQueryAsync();
        }

        await using var del = c.CreateCommand();
        del.CommandText = "DELETE FROM ReactBuilders WHERE Id=@id AND OwnerUserId=@owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
        Add(del, "@id", payload.Id);
        Add(del, "@owner", ctx.OwnerUserId);
        AddCompany(del, "@company", ctx.CompanyId, isSqlite);
        if (await del.ExecuteNonQueryAsync() <= 0) return NotFoundJson("Record not found.");

        return new JsonResult(new { success = true, message = "React builder deleted." });
    }

    public async Task<IActionResult> OnPostSaveConnectionsAsync([FromBody] SaveConnectionsPayload payload)
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return UnauthorizedJson();
        if (!ctx.CanManage) return ForbiddenJson("You do not have permission to save page connections.");
        if (payload.PageId <= 0) return BadJson("Invalid page id.");

        await EnsureReactBuilderTablesAsync();
        await EnsurePagesTableAsync();
        if (!await PageInScopeAsync(payload.PageId, ctx.OwnerUserId, ctx.CompanyId)) return NotFoundJson("Page not found.");

        var ids = new[] { payload.TopReactBuilderId, payload.MenuReactBuilderId, payload.BodyReactBuilderId, payload.SubtemplateReactBuilderId }
            .Where(x => x.HasValue && x.Value > 0).Select(x => x!.Value).Distinct().ToList();
        if (ids.Count > 0)
        {
            var owned = await OwnedBuilderIdsAsync(ctx.OwnerUserId, ctx.CompanyId);
            if (ids.Any(id => !owned.Contains(id))) return BadJson("One or more selected builders were not found.");
        }

        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();
        var existing = await LoadConnectionAsync(ctx.OwnerUserId, ctx.CompanyId, payload.PageId);

        if (existing == null)
        {
            await using var ins = c.CreateCommand();
            ins.CommandText = @"INSERT INTO ReactPageConnections (OwnerUserId, CompanyId, PageId, TopReactBuilderId, MenuReactBuilderId, BodyReactBuilderId, SubtemplateReactBuilderId, UpdatedAtUtc)
VALUES (@owner, @company, @page, @top, @menu, @body, @subtemplate, @updated)";
            Add(ins, "@owner", ctx.OwnerUserId);
            AddCompany(ins, "@company", ctx.CompanyId, isSqlite);
            Add(ins, "@page", payload.PageId);
            Add(ins, "@top", payload.TopReactBuilderId.HasValue ? payload.TopReactBuilderId.Value : DBNull.Value);
            Add(ins, "@menu", payload.MenuReactBuilderId.HasValue ? payload.MenuReactBuilderId.Value : DBNull.Value);
            Add(ins, "@body", payload.BodyReactBuilderId.HasValue ? payload.BodyReactBuilderId.Value : DBNull.Value);
            Add(ins, "@subtemplate", payload.SubtemplateReactBuilderId.HasValue ? payload.SubtemplateReactBuilderId.Value : DBNull.Value);
            Add(ins, "@updated", DateTime.UtcNow);
            await ins.ExecuteNonQueryAsync();
        }
        else
        {
            await using var upd = c.CreateCommand();
            upd.CommandText = @"UPDATE ReactPageConnections
SET TopReactBuilderId=@top, MenuReactBuilderId=@menu, BodyReactBuilderId=@body, SubtemplateReactBuilderId=@subtemplate, UpdatedAtUtc=@updated
WHERE Id=@id";
            Add(upd, "@top", payload.TopReactBuilderId.HasValue ? payload.TopReactBuilderId.Value : DBNull.Value);
            Add(upd, "@menu", payload.MenuReactBuilderId.HasValue ? payload.MenuReactBuilderId.Value : DBNull.Value);
            Add(upd, "@body", payload.BodyReactBuilderId.HasValue ? payload.BodyReactBuilderId.Value : DBNull.Value);
            Add(upd, "@subtemplate", payload.SubtemplateReactBuilderId.HasValue ? payload.SubtemplateReactBuilderId.Value : DBNull.Value);
            Add(upd, "@updated", DateTime.UtcNow);
            Add(upd, "@id", existing.Id);
            await upd.ExecuteNonQueryAsync();
        }

        return new JsonResult(new { success = true, message = "Page connections saved." });
    }

    public async Task<IActionResult> OnPostDatabaseSyncAsync()
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return UnauthorizedJson();
        await EnsureReactBuilderTablesAsync();
        await EnsurePagesTableAsync();

        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();
        var updated = 0;

        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"UPDATE ReactBuilders
SET CompanyId=@company, UpdatedAtUtc=@updated
WHERE OwnerUserId=@owner AND CompanyId IS NULL";
            Add(cmd, "@owner", ctx.OwnerUserId);
            AddCompany(cmd, "@company", ctx.CompanyId, isSqlite);
            Add(cmd, "@updated", DateTime.UtcNow);
            updated += await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "UPDATE ReactBuilderFiles SET CompanyId=@company, UpdatedAtUtc=@updated WHERE OwnerUserId=@owner AND CompanyId IS NULL";
            Add(cmd, "@owner", ctx.OwnerUserId);
            AddCompany(cmd, "@company", ctx.CompanyId, isSqlite);
            Add(cmd, "@updated", DateTime.UtcNow);
            updated += await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "UPDATE ReactPageConnections SET CompanyId=@company, UpdatedAtUtc=@updated WHERE OwnerUserId=@owner AND CompanyId IS NULL";
            Add(cmd, "@owner", ctx.OwnerUserId);
            AddCompany(cmd, "@company", ctx.CompanyId, isSqlite);
            Add(cmd, "@updated", DateTime.UtcNow);
            updated += await cmd.ExecuteNonQueryAsync();
        }

        return new JsonResult(new { success = true, message = $"Database synced. {updated} record(s) repaired." });
    }

    private async Task<List<ReactBuilderRow>> LoadReactBuildersAsync(string owner, Guid? company)
    {
        var list = new List<ReactBuilderRow>();
        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();

        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = @"SELECT Id, Name, Description, DGUID, EntryFilePath, PreviewMasterpageId, IsActive, CreatedAtUtc, UpdatedAtUtc
FROM ReactBuilders WHERE OwnerUserId=@owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)
ORDER BY UpdatedAtUtc DESC";
            Add(cmd, "@owner", owner);
            AddCompany(cmd, "@company", company, isSqlite);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new ReactBuilderRow
                {
                    Id = r.GetInt32(0),
                    Name = r.IsDBNull(1) ? string.Empty : r.GetString(1),
                    Description = r.IsDBNull(2) ? string.Empty : r.GetString(2),
                    Dguid = ReadGuid(r, 3, isSqlite),
                    EntryFilePath = r.IsDBNull(4) ? "App.jsx" : r.GetString(4),
                    PreviewMasterpageId = r.IsDBNull(5) ? null : r.GetInt32(5),
                    IsActive = r.IsDBNull(6) ? true : (isSqlite ? r.GetInt32(6) == 1 : r.GetBoolean(6)),
                    CreatedAtUtc = ReadUtc(r, 7, isSqlite),
                    UpdatedAtUtc = ReadUtc(r, 8, isSqlite)
                });
            }
        }

        var links = await LoadLinkedCountsAsync(owner, company);
        foreach (var b in list) if (links.TryGetValue(b.Id, out var c2)) b.LinkedPagesCount = c2;
        return list;
    }

    private async Task<Dictionary<int, int>> LoadLinkedCountsAsync(string owner, Guid? company)
    {
        var map = new Dictionary<int, int>();
        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();

        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT TopReactBuilderId, MenuReactBuilderId, BodyReactBuilderId, SubtemplateReactBuilderId FROM ReactPageConnections WHERE OwnerUserId=@owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
        Add(cmd, "@owner", owner);
        AddCompany(cmd, "@company", company, isSqlite);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var set = new HashSet<int>();
            for (var i = 0; i < 4; i++) if (!r.IsDBNull(i)) set.Add(r.GetInt32(i));
            foreach (var id in set)
            {
                map.TryGetValue(id, out var cur);
                map[id] = cur + 1;
            }
        }

        return map;
    }

    private async Task<List<PageOptionRow>> LoadPagesAsync(string owner, Guid? company)
    {
        var list = new List<PageOptionRow>();
        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();

        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Slug FROM Pages WHERE OwnerUserId=@owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) ORDER BY Name";
        Add(cmd, "@owner", owner);
        AddCompany(cmd, "@company", company, isSqlite);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new PageOptionRow { Id = r.GetInt32(0), Name = r.IsDBNull(1) ? string.Empty : r.GetString(1), Slug = r.IsDBNull(2) ? string.Empty : r.GetString(2) });
        return list;
    }

    private async Task<ReactPageConnectionRow?> LoadConnectionAsync(string owner, Guid? company, int pageId)
    {
        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();

        await using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT Id, PageId, TopReactBuilderId, MenuReactBuilderId, BodyReactBuilderId, SubtemplateReactBuilderId
FROM ReactPageConnections
WHERE OwnerUserId=@owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company) AND PageId=@page";
        Add(cmd, "@owner", owner);
        AddCompany(cmd, "@company", company, isSqlite);
        Add(cmd, "@page", pageId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new ReactPageConnectionRow
        {
            Id = r.GetInt32(0),
            PageId = r.GetInt32(1),
            TopReactBuilderId = r.IsDBNull(2) ? null : r.GetInt32(2),
            MenuReactBuilderId = r.IsDBNull(3) ? null : r.GetInt32(3),
            BodyReactBuilderId = r.IsDBNull(4) ? null : r.GetInt32(4),
            SubtemplateReactBuilderId = r.IsDBNull(5) ? null : r.GetInt32(5)
        };
    }

    private async Task<bool> PageInScopeAsync(int pageId, string owner, Guid? company)
    {
        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();

        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM Pages WHERE Id=@id AND OwnerUserId=@owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
        Add(cmd, "@id", pageId);
        Add(cmd, "@owner", owner);
        AddCompany(cmd, "@company", company, isSqlite);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private async Task<HashSet<int>> OwnedBuilderIdsAsync(string owner, Guid? company)
    {
        var set = new HashSet<int>();
        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();

        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id FROM ReactBuilders WHERE OwnerUserId=@owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
        Add(cmd, "@owner", owner);
        AddCompany(cmd, "@company", company, isSqlite);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) set.Add(r.GetInt32(0));
        return set;
    }

    private async Task<int> LastInsertIdAsync(DbConnection c, bool isSqlite)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = isSqlite ? "SELECT last_insert_rowid()" : "SELECT CAST(SCOPE_IDENTITY() AS INT)";
        var v = await cmd.ExecuteScalarAsync();
        return v == null || v is DBNull ? 0 : Convert.ToInt32(v);
    }

    private async Task InsertDefaultWorkspaceAsync(DbConnection c, bool isSqlite, AccessContext ctx, int builderId, DateTime now)
    {
        var nodes = new (string path, string nodeType, string language, string? content, int sortOrder, bool ro)[]
        {
            ("App.jsx", "file", "javascript", "import React from 'react';\nimport './src/styles/app.css';\nimport HomePage from './src/pages/HomePage';\n\nexport default function App(){\n  return <HomePage />;\n}\n", 0, false),
            ("src", "folder", "plaintext", null, 10, false),
            ("src/components", "folder", "plaintext", null, 20, false),
            ("src/pages", "folder", "plaintext", null, 30, false),
            ("src/styles", "folder", "plaintext", null, 40, false),
            ("src/utils", "folder", "plaintext", null, 50, false),
            ("src/components/HeroCard.jsx", "file", "javascript", "import React from 'react';\n\nexport default function HeroCard({ title, subtitle }) {\n  return (\n    <section style={{padding:'1.2rem',borderRadius:'14px',background:'linear-gradient(135deg,#0f172a,#0a2d39)',color:'#e6f4ff'}}>\n      <div style={{fontSize:'.74rem',textTransform:'uppercase',letterSpacing:'.1em',color:'#6ee7b7'}}>Bugence Sample</div>\n      <h1 style={{margin:'.4rem 0 .3rem',fontSize:'1.6rem'}}>{title}</h1>\n      <p style={{margin:0,opacity:.88}}>{subtitle}</p>\n    </section>\n  );\n}\n", 60, false),
            ("src/pages/HomePage.jsx", "file", "javascript", "import React from 'react';\nimport HeroCard from '../components/HeroCard';\nimport { formatUtcNow } from '../utils/format';\n\nexport default function HomePage(){\n  return (\n    <main style={{padding:'20px',display:'grid',gap:'12px',background:'#020617',minHeight:'100vh'}}>\n      <HeroCard title='Bugence React Builder' subtitle='Live preview is reading from your DB workspace.' />\n      <div style={{padding:'12px 14px',border:'1px solid rgba(125,211,252,.35)',borderRadius:'12px',color:'#cfe6ff'}}>\n        Rendered at: {formatUtcNow()}\n      </div>\n    </main>\n  );\n}\n", 70, false),
            ("src/styles/app.css", "file", "css", ":root { color-scheme: dark; }\nbody { margin: 0; background: #020617; }\n", 80, false),
            ("src/utils/format.js", "file", "javascript", "export function formatUtcNow(){\n  return new Date().toISOString().replace('T', ' ').replace('Z', ' UTC');\n}\n", 90, false)
        };

        foreach (var n in nodes)
        {
            await using var cmd = c.CreateCommand();
            cmd.CommandText = @"INSERT INTO ReactBuilderFiles (ReactBuilderId, OwnerUserId, CompanyId, DGUID, Path, NodeType, Language, Content, SortOrder, IsReadOnly, CreatedAtUtc, UpdatedAtUtc)
VALUES (@builder, @owner, @company, @dguid, @path, @type, @lang, @content, @sort, @readonly, @created, @updated)";
            Add(cmd, "@builder", builderId);
            Add(cmd, "@owner", ctx.OwnerUserId);
            AddCompany(cmd, "@company", ctx.CompanyId, isSqlite);
            Add(cmd, "@dguid", isSqlite ? Guid.NewGuid().ToString("N") : Guid.NewGuid());
            Add(cmd, "@path", n.path);
            Add(cmd, "@type", n.nodeType);
            Add(cmd, "@lang", n.language);
            Add(cmd, "@content", n.content == null ? DBNull.Value : n.content);
            Add(cmd, "@sort", n.sortOrder);
            Add(cmd, "@readonly", n.ro);
            Add(cmd, "@created", now);
            Add(cmd, "@updated", now);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task RepairLegacyCompanyScopeAsync(AccessContext ctx)
    {
        if (!ctx.CompanyId.HasValue)
        {
            return;
        }

        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();
        var now = DateTime.UtcNow;

        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "UPDATE ReactBuilders SET CompanyId=@company, UpdatedAtUtc=@updated WHERE OwnerUserId=@owner AND CompanyId IS NULL";
            Add(cmd, "@owner", ctx.OwnerUserId);
            AddCompany(cmd, "@company", ctx.CompanyId, isSqlite);
            Add(cmd, "@updated", now);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "UPDATE ReactBuilderFiles SET CompanyId=@company, UpdatedAtUtc=@updated WHERE OwnerUserId=@owner AND CompanyId IS NULL";
            Add(cmd, "@owner", ctx.OwnerUserId);
            AddCompany(cmd, "@company", ctx.CompanyId, isSqlite);
            Add(cmd, "@updated", now);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "UPDATE ReactPageConnections SET CompanyId=@company, UpdatedAtUtc=@updated WHERE OwnerUserId=@owner AND CompanyId IS NULL";
            Add(cmd, "@owner", ctx.OwnerUserId);
            AddCompany(cmd, "@company", ctx.CompanyId, isSqlite);
            Add(cmd, "@updated", now);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task EnsureReactBuilderTablesAsync()
    {
        if (IsSqlite())
        {
            await _db.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ReactBuilders (Id INTEGER PRIMARY KEY AUTOINCREMENT, OwnerUserId TEXT NOT NULL, CompanyId TEXT NULL, DGUID TEXT NOT NULL, Name TEXT NOT NULL, Description TEXT NULL, EntryFilePath TEXT NOT NULL DEFAULT 'App.jsx', PreviewMasterpageId INTEGER NULL, IsActive INTEGER NOT NULL DEFAULT 1, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL);");
            await _db.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ReactBuilderFiles (Id INTEGER PRIMARY KEY AUTOINCREMENT, ReactBuilderId INTEGER NOT NULL, OwnerUserId TEXT NOT NULL, CompanyId TEXT NULL, DGUID TEXT NOT NULL, Path TEXT NOT NULL, NodeType TEXT NOT NULL, Language TEXT NOT NULL, Content TEXT NULL, SortOrder INTEGER NOT NULL DEFAULT 0, IsReadOnly INTEGER NOT NULL DEFAULT 0, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL);");
            await _db.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ReactPageConnections (Id INTEGER PRIMARY KEY AUTOINCREMENT, OwnerUserId TEXT NOT NULL, CompanyId TEXT NULL, PageId INTEGER NOT NULL, TopReactBuilderId INTEGER NULL, MenuReactBuilderId INTEGER NULL, BodyReactBuilderId INTEGER NULL, SubtemplateReactBuilderId INTEGER NULL, UpdatedAtUtc TEXT NOT NULL);");
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ReactBuilders' AND xtype='U') CREATE TABLE ReactBuilders (Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY, OwnerUserId NVARCHAR(450) NOT NULL, CompanyId UNIQUEIDENTIFIER NULL, DGUID UNIQUEIDENTIFIER NOT NULL, Name NVARCHAR(200) NOT NULL, Description NVARCHAR(MAX) NULL, EntryFilePath NVARCHAR(400) NOT NULL DEFAULT('App.jsx'), PreviewMasterpageId INT NULL, IsActive BIT NOT NULL DEFAULT 1, CreatedAtUtc DATETIME2 NOT NULL, UpdatedAtUtc DATETIME2 NOT NULL);");
        await _db.Database.ExecuteSqlRawAsync(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ReactBuilderFiles' AND xtype='U') CREATE TABLE ReactBuilderFiles (Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY, ReactBuilderId INT NOT NULL, OwnerUserId NVARCHAR(450) NOT NULL, CompanyId UNIQUEIDENTIFIER NULL, DGUID UNIQUEIDENTIFIER NOT NULL, Path NVARCHAR(600) NOT NULL, NodeType NVARCHAR(20) NOT NULL, Language NVARCHAR(60) NOT NULL, Content NVARCHAR(MAX) NULL, SortOrder INT NOT NULL DEFAULT 0, IsReadOnly BIT NOT NULL DEFAULT 0, CreatedAtUtc DATETIME2 NOT NULL, UpdatedAtUtc DATETIME2 NOT NULL);");
        await _db.Database.ExecuteSqlRawAsync(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ReactPageConnections' AND xtype='U') CREATE TABLE ReactPageConnections (Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY, OwnerUserId NVARCHAR(450) NOT NULL, CompanyId UNIQUEIDENTIFIER NULL, PageId INT NOT NULL, TopReactBuilderId INT NULL, MenuReactBuilderId INT NULL, BodyReactBuilderId INT NULL, SubtemplateReactBuilderId INT NULL, UpdatedAtUtc DATETIME2 NOT NULL);");
    }

    private async Task EnsurePagesTableAsync()
    {
        if (IsSqlite())
        {
            await _db.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS Pages (Id INTEGER PRIMARY KEY AUTOINCREMENT, OwnerUserId TEXT NOT NULL, CompanyId TEXT NULL, DGUID TEXT NOT NULL, Name TEXT NOT NULL, Slug TEXT NOT NULL, MasterpageId INTEGER NULL, TemplateViewerId INTEGER NULL, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL);");
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Pages' AND xtype='U') CREATE TABLE Pages (Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY, OwnerUserId NVARCHAR(450) NOT NULL, CompanyId UNIQUEIDENTIFIER NULL, DGUID UNIQUEIDENTIFIER NOT NULL, Name NVARCHAR(200) NOT NULL, Slug NVARCHAR(200) NOT NULL, MasterpageId INT NULL, TemplateViewerId INT NULL, CreatedAtUtc DATETIME2 NOT NULL, UpdatedAtUtc DATETIME2 NOT NULL);");
    }

    private async Task<AccessContext?> GetAccessContextAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;

        var member = await _db.TeamMembers.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == user.Id);
        var owner = member?.OwnerUserId ?? user.Id;
        var ownerCompany = await _db.Users.AsNoTracking().Where(u => u.Id == owner).Select(u => u.CompanyId).FirstOrDefaultAsync();
        return new AccessContext
        {
            User = user,
            OwnerUserId = owner,
            CompanyId = ownerCompany ?? user.CompanyId,
            CanManage = member == null || string.Equals(member.Role, "Admin", StringComparison.OrdinalIgnoreCase)
        };
    }

    private bool IsSqlite() => (_db.Database.ProviderName ?? string.Empty).Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
    private static string NormalizeEntryPath(string? p) => string.IsNullOrWhiteSpace(NormalizePath(p)) ? "App.jsx" : NormalizePath(p);
    private static string NormalizePath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return string.Empty;
        var x = p.Trim().Replace('\\', '/');
        while (x.Contains("//", StringComparison.Ordinal)) x = x.Replace("//", "/", StringComparison.Ordinal);
        if (x.StartsWith("/", StringComparison.Ordinal) || x.Contains(":", StringComparison.Ordinal)) return string.Empty;
        var segs = x.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segs.Length == 0 || segs.Any(s => s == "." || s == "..")) return string.Empty;
        return string.Join('/', segs);
    }

    private static JsonSerializerOptions JsonOpts() => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = JavaScriptEncoder.Default, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private static DateTime ReadUtc(IDataRecord r, int i, bool sqlite) => r.IsDBNull(i) ? DateTime.UtcNow : (sqlite ? (DateTime.TryParse(r.GetValue(i)?.ToString(), out var dt) ? dt : DateTime.UtcNow) : Convert.ToDateTime(r.GetValue(i)));
    private static string ReadGuid(IDataRecord r, int i, bool sqlite) { if (r.IsDBNull(i)) return string.Empty; if (sqlite) return r.GetString(i); var v = r.GetValue(i); return v is Guid g ? g.ToString() : v.ToString() ?? string.Empty; }
    private static string GetInitials(string n) { var i = new string(n.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => char.ToUpperInvariant(x[0])).Take(2).ToArray()); return string.IsNullOrWhiteSpace(i) ? "AD" : i; }

    private static void Add(IDbCommand c, string n, object? v) { var p = c.CreateParameter(); p.ParameterName = n; p.Value = v ?? DBNull.Value; c.Parameters.Add(p); }
    private static void AddCompany(IDbCommand c, string n, Guid? id, bool sqlite) => Add(c, n, id.HasValue ? (sqlite ? id.Value.ToString() : id.Value) : null);

    private JsonResult UnauthorizedJson() => new(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
    private JsonResult ForbiddenJson(string m) => new(new { success = false, message = m }) { StatusCode = 403 };
    private JsonResult BadJson(string m) => new(new { success = false, message = m }) { StatusCode = 400 };
    private JsonResult NotFoundJson(string m) => new(new { success = false, message = m }) { StatusCode = 404 };

    private sealed class AccessContext { public required ApplicationUser User { get; init; } public required string OwnerUserId { get; init; } public Guid? CompanyId { get; init; } public bool CanManage { get; init; } }
    private sealed class ReactPageConnectionRow { public int Id { get; set; } public int PageId { get; set; } public int? TopReactBuilderId { get; set; } public int? MenuReactBuilderId { get; set; } public int? BodyReactBuilderId { get; set; } public int? SubtemplateReactBuilderId { get; set; } }

    public class ReactBuilderRow { public int Id { get; set; } public int DisplayId { get; set; } public string Name { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public string Dguid { get; set; } = string.Empty; public string EntryFilePath { get; set; } = "App.jsx"; public int? PreviewMasterpageId { get; set; } public int LinkedPagesCount { get; set; } public bool IsActive { get; set; } public DateTime CreatedAtUtc { get; set; } public DateTime UpdatedAtUtc { get; set; } }
    public class PageOptionRow { public int Id { get; set; } public string Name { get; set; } = string.Empty; public string Slug { get; set; } = string.Empty; }

    public class ReactBuilderPayload { public int Id { get; set; } [Required] public string? Name { get; set; } public string? Description { get; set; } public string? EntryFilePath { get; set; } public int? PreviewMasterpageId { get; set; } public bool? IsActive { get; set; } }
    public class DeletePayload { public int Id { get; set; } }
    public class SaveConnectionsPayload { public int PageId { get; set; } public int? TopReactBuilderId { get; set; } public int? MenuReactBuilderId { get; set; } public int? BodyReactBuilderId { get; set; } public int? SubtemplateReactBuilderId { get; set; } }
}

