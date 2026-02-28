using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Common;
using System.IO.Compression;
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
public class ReactBuilderEditorModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public ReactBuilderEditorModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public int BuilderId { get; private set; }
    public string BuilderName { get; private set; } = "React Builder";
    public string EntryFilePath { get; private set; } = "App.jsx";
    public bool CanManage { get; private set; }
    public string WorkspaceJson { get; private set; } = "{}";

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return RedirectToPage("/Auth/Login");

        await EnsureReactBuilderTablesAsync();
        var builder = await LoadBuilderAsync(id, ctx.OwnerUserId, ctx.CompanyId);
        if (builder == null) return RedirectToPage("/Tools/ReactBuilder");

        BuilderId = builder.Id;
        BuilderName = builder.Name;
        EntryFilePath = builder.EntryFilePath;
        CanManage = ctx.CanManage;

        var files = await LoadFilesAsync(id, ctx.OwnerUserId, ctx.CompanyId);
        WorkspaceJson = JsonSerializer.Serialize(new { id = builder.Id, name = builder.Name, entryFilePath = builder.EntryFilePath, files }, JsonOpts());
        return Page();
    }

    public async Task<IActionResult> OnGetWorkspaceAsync(int id)
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return UnauthorizedJson();
        await EnsureReactBuilderTablesAsync();

        var builder = await LoadBuilderAsync(id, ctx.OwnerUserId, ctx.CompanyId);
        if (builder == null) return NotFoundJson("Builder not found.");
        var files = await LoadFilesAsync(id, ctx.OwnerUserId, ctx.CompanyId);
        return new JsonResult(new { success = true, workspace = new { id = builder.Id, name = builder.Name, entryFilePath = builder.EntryFilePath, files } });
    }

    public async Task<IActionResult> OnPostSaveFileAsync([FromBody] SaveFilePayload payload)
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return UnauthorizedJson();
        if (!ctx.CanManage) return ForbiddenJson("You do not have permission to edit builders.");

        await EnsureReactBuilderTablesAsync();
        var builder = await LoadBuilderAsync(payload.ReactBuilderId, ctx.OwnerUserId, ctx.CompanyId);
        if (builder == null) return NotFoundJson("Builder not found.");

        var path = NormalizePath(payload.Path);
        if (string.IsNullOrWhiteSpace(path) || path.EndsWith("/", StringComparison.Ordinal)) return BadJson("Invalid file path.");

        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();

        await using var cmd = c.CreateCommand();
        cmd.CommandText = @"UPDATE ReactBuilderFiles
SET Content=@content, Language=@lang, UpdatedAtUtc=@updated
WHERE ReactBuilderId=@builder AND Path=@path AND NodeType='file' AND OwnerUserId=@owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
        Add(cmd, "@content", payload.Content ?? string.Empty);
        Add(cmd, "@lang", string.IsNullOrWhiteSpace(payload.Language) ? GuessLanguage(path) : payload.Language!.Trim());
        Add(cmd, "@updated", DateTime.UtcNow);
        Add(cmd, "@builder", payload.ReactBuilderId);
        Add(cmd, "@path", path);
        Add(cmd, "@owner", ctx.OwnerUserId);
        AddCompany(cmd, "@company", ctx.CompanyId, isSqlite);
        if (await cmd.ExecuteNonQueryAsync() <= 0) return NotFoundJson("File node not found.");

        return new JsonResult(new { success = true, message = "File saved." });
    }

    public async Task<IActionResult> OnPostSaveWorkspaceAsync([FromBody] SaveWorkspacePayload payload)
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return UnauthorizedJson();
        if (!ctx.CanManage) return ForbiddenJson("You do not have permission to edit builders.");

        await EnsureReactBuilderTablesAsync();
        var builder = await LoadBuilderAsync(payload.ReactBuilderId, ctx.OwnerUserId, ctx.CompanyId);
        if (builder == null) return NotFoundJson("Builder not found.");

        var normalized = NormalizeWorkspaceNodes(payload.Files ?? new List<WorkspaceNode>(), out var error);
        if (error != null) return BadJson(error);
        await PersistWorkspaceAsync(payload.ReactBuilderId, ctx, normalized);
        return new JsonResult(new { success = true, message = "Workspace saved." });
    }

    public async Task<IActionResult> OnPostImportWorkspaceAsync(int reactBuilderId, List<IFormFile> upload)
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return UnauthorizedJson();
        if (!ctx.CanManage) return ForbiddenJson("You do not have permission to import builders.");

        await EnsureReactBuilderTablesAsync();
        var builder = await LoadBuilderAsync(reactBuilderId, ctx.OwnerUserId, ctx.CompanyId);
        if (builder == null) return NotFoundJson("Builder not found.");
        if (upload == null || upload.Count == 0) return BadJson("Select a local folder or files to import.");

        var nodes = new List<WorkspaceNode>();
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in upload.Where(f => f != null && f.Length > 0))
        {
            var path = NormalizePath(file.FileName);
            if (string.IsNullOrWhiteSpace(path)) continue;

            await using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();

            nodes.Add(new WorkspaceNode
            {
                Path = path,
                NodeType = "file",
                Language = GuessLanguage(path),
                Content = content
            });

            var parent = ParentPath(path);
            while (!string.IsNullOrWhiteSpace(parent))
            {
                if (!folders.Add(parent)) break;
                parent = ParentPath(parent);
            }
        }

        foreach (var folder in folders.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            nodes.Add(new WorkspaceNode
            {
                Path = folder,
                NodeType = "folder",
                Language = "plaintext"
            });
        }

        if (nodes.Count == 0) return BadJson("No valid files were found in the selected upload.");

        var normalized = NormalizeWorkspaceNodes(nodes, out var error);
        if (error != null) return BadJson(error);
        await PersistWorkspaceAsync(reactBuilderId, ctx, normalized);

        return new JsonResult(new { success = true, message = "Local workspace imported." });
    }

    public async Task<IActionResult> OnGetDownloadWorkspaceAsync(int id)
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return Content("Unauthorized", "text/plain");
        await EnsureReactBuilderTablesAsync();

        var builder = await LoadBuilderAsync(id, ctx.OwnerUserId, ctx.CompanyId);
        if (builder == null) return Content("Builder not found", "text/plain");
        var files = await LoadFilesAsync(id, ctx.OwnerUserId, ctx.CompanyId);

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files.Where(f => string.Equals(f.NodeType, "file", StringComparison.OrdinalIgnoreCase)))
            {
                var entry = archive.CreateEntry(file.Path, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await using var writer = new StreamWriter(entryStream);
                await writer.WriteAsync(file.Content ?? string.Empty);
            }
        }

        ms.Position = 0;
        var safeName = string.Concat((builder.Name ?? "react-builder").Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-');
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "react-builder";
        return File(ms.ToArray(), "application/zip", $"{safeName}.zip");
    }

    public async Task<IActionResult> OnPostCreateNodeAsync([FromBody] CreateNodePayload payload)
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return UnauthorizedJson();
        if (!ctx.CanManage) return ForbiddenJson("You do not have permission to edit builders.");

        await EnsureReactBuilderTablesAsync();
        if (await LoadBuilderAsync(payload.ReactBuilderId, ctx.OwnerUserId, ctx.CompanyId) == null) return NotFoundJson("Builder not found.");

        var nodeType = string.Equals(payload.NodeType, "folder", StringComparison.OrdinalIgnoreCase) ? "folder" : "file";
        var parent = NormalizePath(payload.ParentPath);
        var name = (payload.Name ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(name) || name.Contains("/", StringComparison.Ordinal)) return BadJson("Invalid node name.");

        var path = string.IsNullOrWhiteSpace(parent) ? name : $"{parent}/{name}";
        path = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(path)) return BadJson("Invalid node path.");
        if (nodeType == "file" && path.EndsWith("/", StringComparison.Ordinal)) return BadJson("Invalid file path.");

        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();

        await using (var exists = c.CreateCommand())
        {
            exists.CommandText = "SELECT COUNT(1) FROM ReactBuilderFiles WHERE ReactBuilderId=@builder AND Path=@path AND OwnerUserId=@owner";
            Add(exists, "@builder", payload.ReactBuilderId);
            Add(exists, "@path", path);
            Add(exists, "@owner", ctx.OwnerUserId);
            if (Convert.ToInt32(await exists.ExecuteScalarAsync()) > 0) return BadJson("A node with that path already exists.");
        }

        await using var ins = c.CreateCommand();
        ins.CommandText = @"INSERT INTO ReactBuilderFiles (ReactBuilderId, OwnerUserId, CompanyId, DGUID, Path, NodeType, Language, Content, SortOrder, IsReadOnly, CreatedAtUtc, UpdatedAtUtc)
VALUES (@builder, @owner, @company, @dguid, @path, @type, @lang, @content, @sort, @readonly, @created, @updated)";
        Add(ins, "@builder", payload.ReactBuilderId);
        Add(ins, "@owner", ctx.OwnerUserId);
        AddCompany(ins, "@company", ctx.CompanyId, isSqlite);
        Add(ins, "@dguid", isSqlite ? Guid.NewGuid().ToString("N") : Guid.NewGuid());
        Add(ins, "@path", path);
        Add(ins, "@type", nodeType);
        Add(ins, "@lang", nodeType == "file" ? GuessLanguage(path) : "plaintext");
        Add(ins, "@content", nodeType == "file" ? string.Empty : DBNull.Value);
        Add(ins, "@sort", 0);
        Add(ins, "@readonly", false);
        Add(ins, "@created", DateTime.UtcNow);
        Add(ins, "@updated", DateTime.UtcNow);
        await ins.ExecuteNonQueryAsync();

        return new JsonResult(new { success = true, message = "Node created." });
    }

    public async Task<IActionResult> OnPostRenameNodeAsync([FromBody] RenameNodePayload payload)
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return UnauthorizedJson();
        if (!ctx.CanManage) return ForbiddenJson("You do not have permission to edit builders.");

        await EnsureReactBuilderTablesAsync();
        if (await LoadBuilderAsync(payload.ReactBuilderId, ctx.OwnerUserId, ctx.CompanyId) == null) return NotFoundJson("Builder not found.");

        var oldPath = NormalizePath(payload.Path);
        var newPath = NormalizePath(payload.NewPath);
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath)) return BadJson("Invalid node path.");

        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();

        await using var tx = await c.BeginTransactionAsync();

        var nodes = await LoadFilesAsync(payload.ReactBuilderId, ctx.OwnerUserId, ctx.CompanyId, c, tx);
        var exact = nodes.FirstOrDefault(n => string.Equals(n.Path, oldPath, StringComparison.OrdinalIgnoreCase));
        if (exact == null) return NotFoundJson("Node not found.");

        var updates = nodes
            .Where(n => string.Equals(n.Path, oldPath, StringComparison.OrdinalIgnoreCase) || n.Path.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase))
            .Select(n => new { old = n.Path, next = string.Equals(n.Path, oldPath, StringComparison.OrdinalIgnoreCase) ? newPath : (newPath + n.Path[oldPath.Length..]) })
            .ToList();

        var collision = updates.Any(u => nodes.Any(n => !string.Equals(n.Path, u.old, StringComparison.OrdinalIgnoreCase) && string.Equals(n.Path, u.next, StringComparison.OrdinalIgnoreCase)));
        if (collision) return BadJson("A node already exists at the target path.");

        foreach (var u in updates)
        {
            await using var upd = c.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE ReactBuilderFiles SET Path=@next, UpdatedAtUtc=@updated WHERE ReactBuilderId=@builder AND Path=@old AND OwnerUserId=@owner";
            Add(upd, "@next", u.next);
            Add(upd, "@updated", DateTime.UtcNow);
            Add(upd, "@builder", payload.ReactBuilderId);
            Add(upd, "@old", u.old);
            Add(upd, "@owner", ctx.OwnerUserId);
            await upd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return new JsonResult(new { success = true, message = "Node renamed." });
    }

    public async Task<IActionResult> OnPostDeleteNodeAsync([FromBody] DeleteNodePayload payload)
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return UnauthorizedJson();
        if (!ctx.CanManage) return ForbiddenJson("You do not have permission to edit builders.");

        await EnsureReactBuilderTablesAsync();
        if (await LoadBuilderAsync(payload.ReactBuilderId, ctx.OwnerUserId, ctx.CompanyId) == null) return NotFoundJson("Builder not found.");

        var path = NormalizePath(payload.Path);
        if (string.IsNullOrWhiteSpace(path)) return BadJson("Invalid node path.");

        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();

        await using var cmd = c.CreateCommand();
        cmd.CommandText = @"DELETE FROM ReactBuilderFiles
WHERE ReactBuilderId=@builder AND OwnerUserId=@owner
AND (Path=@path OR Path LIKE @prefix)";
        Add(cmd, "@builder", payload.ReactBuilderId);
        Add(cmd, "@owner", ctx.OwnerUserId);
        Add(cmd, "@path", path);
        Add(cmd, "@prefix", path + "/%");
        await cmd.ExecuteNonQueryAsync();

        return new JsonResult(new { success = true, message = "Node deleted." });
    }

    public async Task<IActionResult> OnGetPreviewDocumentAsync(int id, string? entryPath)
    {
        var ctx = await GetAccessContextAsync();
        if (ctx == null) return Content("Unauthorized", "text/plain");
        await EnsureReactBuilderTablesAsync();

        var builder = await LoadBuilderAsync(id, ctx.OwnerUserId, ctx.CompanyId);
        if (builder == null) return Content("Builder not found", "text/plain");
        var files = await LoadFilesAsync(id, ctx.OwnerUserId, ctx.CompanyId);

        var payload = JsonSerializer.Serialize(new
        {
            entryFilePath = string.IsNullOrWhiteSpace(entryPath) ? builder.EntryFilePath : NormalizePath(entryPath),
            files
        });

        return Content($"<html><body><script type='application/json' id='react-workspace'>{System.Net.WebUtility.HtmlEncode(payload)}</script></body></html>", "text/html");
    }

    private async Task<BuilderRow?> LoadBuilderAsync(int id, string owner, Guid? company)
    {
        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();

        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, EntryFilePath FROM ReactBuilders WHERE Id=@id AND OwnerUserId=@owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)";
        Add(cmd, "@id", id);
        Add(cmd, "@owner", owner);
        AddCompany(cmd, "@company", company, isSqlite);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new BuilderRow { Id = r.GetInt32(0), Name = r.IsDBNull(1) ? string.Empty : r.GetString(1), EntryFilePath = r.IsDBNull(2) ? "App.jsx" : r.GetString(2) };
    }

    private async Task<List<FileRow>> LoadFilesAsync(int id, string owner, Guid? company) => await LoadFilesAsync(id, owner, company, null, null);

    private async Task<List<FileRow>> LoadFilesAsync(int id, string owner, Guid? company, DbConnection? existingConnection, DbTransaction? tx)
    {
        var list = new List<FileRow>();
        var c = existingConnection ?? _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();

        await using var cmd = c.CreateCommand();
        if (tx != null) cmd.Transaction = tx;
        cmd.CommandText = @"SELECT Id, Path, NodeType, Language, Content, SortOrder, IsReadOnly, UpdatedAtUtc
FROM ReactBuilderFiles
WHERE ReactBuilderId=@builder AND OwnerUserId=@owner AND ((@company IS NULL AND CompanyId IS NULL) OR CompanyId = @company)
ORDER BY CASE WHEN NodeType='folder' THEN 0 ELSE 1 END, Path";
        Add(cmd, "@builder", id);
        Add(cmd, "@owner", owner);
        AddCompany(cmd, "@company", company, isSqlite);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            list.Add(new FileRow
            {
                Id = r.GetInt32(0),
                Path = r.IsDBNull(1) ? string.Empty : r.GetString(1),
                NodeType = r.IsDBNull(2) ? "file" : r.GetString(2),
                Language = r.IsDBNull(3) ? "plaintext" : r.GetString(3),
                Content = r.IsDBNull(4) ? null : r.GetString(4),
                SortOrder = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                IsReadOnly = r.IsDBNull(6) ? false : (isSqlite ? r.GetInt32(6) == 1 : r.GetBoolean(6)),
                UpdatedAtUtc = r.IsDBNull(7) ? DateTime.UtcNow : (isSqlite ? (DateTime.TryParse(r.GetValue(7)?.ToString(), out var dt) ? dt : DateTime.UtcNow) : Convert.ToDateTime(r.GetValue(7)))
            });
        }

        return list;
    }

    private async Task EnsureReactBuilderTablesAsync()
    {
        if (IsSqlite())
        {
            await _db.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ReactBuilders (Id INTEGER PRIMARY KEY AUTOINCREMENT, OwnerUserId TEXT NOT NULL, CompanyId TEXT NULL, DGUID TEXT NOT NULL, Name TEXT NOT NULL, Description TEXT NULL, EntryFilePath TEXT NOT NULL DEFAULT 'App.jsx', PreviewMasterpageId INTEGER NULL, IsActive INTEGER NOT NULL DEFAULT 1, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL);");
            await _db.Database.ExecuteSqlRawAsync(@"CREATE TABLE IF NOT EXISTS ReactBuilderFiles (Id INTEGER PRIMARY KEY AUTOINCREMENT, ReactBuilderId INTEGER NOT NULL, OwnerUserId TEXT NOT NULL, CompanyId TEXT NULL, DGUID TEXT NOT NULL, Path TEXT NOT NULL, NodeType TEXT NOT NULL, Language TEXT NOT NULL, Content TEXT NULL, SortOrder INTEGER NOT NULL DEFAULT 0, IsReadOnly INTEGER NOT NULL DEFAULT 0, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL);");
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ReactBuilders' AND xtype='U') CREATE TABLE ReactBuilders (Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY, OwnerUserId NVARCHAR(450) NOT NULL, CompanyId UNIQUEIDENTIFIER NULL, DGUID UNIQUEIDENTIFIER NOT NULL, Name NVARCHAR(200) NOT NULL, Description NVARCHAR(MAX) NULL, EntryFilePath NVARCHAR(400) NOT NULL DEFAULT('App.jsx'), PreviewMasterpageId INT NULL, IsActive BIT NOT NULL DEFAULT 1, CreatedAtUtc DATETIME2 NOT NULL, UpdatedAtUtc DATETIME2 NOT NULL);");
        await _db.Database.ExecuteSqlRawAsync(@"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ReactBuilderFiles' AND xtype='U') CREATE TABLE ReactBuilderFiles (Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY, ReactBuilderId INT NOT NULL, OwnerUserId NVARCHAR(450) NOT NULL, CompanyId UNIQUEIDENTIFIER NULL, DGUID UNIQUEIDENTIFIER NOT NULL, Path NVARCHAR(600) NOT NULL, NodeType NVARCHAR(20) NOT NULL, Language NVARCHAR(60) NOT NULL, Content NVARCHAR(MAX) NULL, SortOrder INT NOT NULL DEFAULT 0, IsReadOnly BIT NOT NULL DEFAULT 0, CreatedAtUtc DATETIME2 NOT NULL, UpdatedAtUtc DATETIME2 NOT NULL);");
    }

    private async Task<AccessContext?> GetAccessContextAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return null;

        var member = await _db.TeamMembers.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == user.Id);
        var owner = member?.OwnerUserId ?? user.Id;
        var ownerCompany = await _db.Users.AsNoTracking().Where(u => u.Id == owner).Select(u => u.CompanyId).FirstOrDefaultAsync();

        return new AccessContext { User = user, OwnerUserId = owner, CompanyId = ownerCompany ?? user.CompanyId, CanManage = member == null || string.Equals(member.Role, "Admin", StringComparison.OrdinalIgnoreCase) };
    }

    private bool IsSqlite() => (_db.Database.ProviderName ?? string.Empty).Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
    private static string ParentPath(string path)
    {
        var normalized = NormalizePath(path);
        var idx = normalized.LastIndexOf('/');
        return idx >= 0 ? normalized[..idx] : string.Empty;
    }

    private List<WorkspaceNode> NormalizeWorkspaceNodes(IEnumerable<WorkspaceNode> source, out string? error)
    {
        var normalized = new List<WorkspaceNode>();
        foreach (var n in source ?? Enumerable.Empty<WorkspaceNode>())
        {
            var p = NormalizePath(n.Path);
            if (string.IsNullOrWhiteSpace(p))
            {
                error = "Invalid node path detected.";
                return [];
            }

            var type = string.Equals(n.NodeType, "folder", StringComparison.OrdinalIgnoreCase) ? "folder" : "file";
            if (type == "folder" && p.EndsWith("/", StringComparison.Ordinal)) p = p.TrimEnd('/');
            if (type == "file" && p.EndsWith("/", StringComparison.Ordinal))
            {
                error = "Invalid file path detected.";
                return [];
            }

            normalized.Add(new WorkspaceNode
            {
                Path = p,
                NodeType = type,
                Language = type == "file" ? (string.IsNullOrWhiteSpace(n.Language) ? GuessLanguage(p) : n.Language!.Trim()) : "plaintext",
                Content = type == "file" ? (n.Content ?? string.Empty) : null,
                SortOrder = n.SortOrder,
                IsReadOnly = n.IsReadOnly
            });
        }

        var distinctPaths = normalized.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (distinctPaths != normalized.Count)
        {
            error = "Duplicate node paths found.";
            return [];
        }

        error = null;
        return normalized
            .OrderBy(x => string.Equals(x.NodeType, "folder", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task PersistWorkspaceAsync(int reactBuilderId, AccessContext ctx, List<WorkspaceNode> normalized)
    {
        using var c = _db.Database.GetDbConnection();
        if (c.State != ConnectionState.Open) await c.OpenAsync();
        var isSqlite = IsSqlite();
        await using var tx = await c.BeginTransactionAsync();

        var existing = await LoadFilesAsync(reactBuilderId, ctx.OwnerUserId, ctx.CompanyId, c, tx);
        var existingSet = new HashSet<string>(existing.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
        var incomingSet = new HashSet<string>(normalized.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);

        foreach (var old in existing.Where(x => !incomingSet.Contains(x.Path)))
        {
            await using var del = c.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM ReactBuilderFiles WHERE ReactBuilderId=@builder AND Path=@path AND OwnerUserId=@owner";
            Add(del, "@builder", reactBuilderId);
            Add(del, "@path", old.Path);
            Add(del, "@owner", ctx.OwnerUserId);
            await del.ExecuteNonQueryAsync();
        }

        var i = 0;
        foreach (var n in normalized)
        {
            if (existingSet.Contains(n.Path))
            {
                await using var upd = c.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = @"UPDATE ReactBuilderFiles
SET NodeType=@type, Language=@lang, Content=@content, SortOrder=@sort, IsReadOnly=@readonly, UpdatedAtUtc=@updated
WHERE ReactBuilderId=@builder AND Path=@path AND OwnerUserId=@owner";
                Add(upd, "@type", n.NodeType);
                Add(upd, "@lang", n.Language);
                Add(upd, "@content", n.Content == null ? DBNull.Value : n.Content);
                Add(upd, "@sort", n.SortOrder == 0 ? i : n.SortOrder);
                Add(upd, "@readonly", n.IsReadOnly);
                Add(upd, "@updated", DateTime.UtcNow);
                Add(upd, "@builder", reactBuilderId);
                Add(upd, "@path", n.Path);
                Add(upd, "@owner", ctx.OwnerUserId);
                await upd.ExecuteNonQueryAsync();
            }
            else
            {
                await using var ins = c.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = @"INSERT INTO ReactBuilderFiles (ReactBuilderId, OwnerUserId, CompanyId, DGUID, Path, NodeType, Language, Content, SortOrder, IsReadOnly, CreatedAtUtc, UpdatedAtUtc)
VALUES (@builder, @owner, @company, @dguid, @path, @type, @lang, @content, @sort, @readonly, @created, @updated)";
                Add(ins, "@builder", reactBuilderId);
                Add(ins, "@owner", ctx.OwnerUserId);
                AddCompany(ins, "@company", ctx.CompanyId, isSqlite);
                Add(ins, "@dguid", isSqlite ? Guid.NewGuid().ToString("N") : Guid.NewGuid());
                Add(ins, "@path", n.Path);
                Add(ins, "@type", n.NodeType);
                Add(ins, "@lang", n.Language);
                Add(ins, "@content", n.Content == null ? DBNull.Value : n.Content);
                Add(ins, "@sort", n.SortOrder == 0 ? i : n.SortOrder);
                Add(ins, "@readonly", n.IsReadOnly);
                Add(ins, "@created", DateTime.UtcNow);
                Add(ins, "@updated", DateTime.UtcNow);
                await ins.ExecuteNonQueryAsync();
            }
            i++;
        }

        await tx.CommitAsync();
    }

    private static string NormalizePath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return string.Empty;
        var x = p.Trim().Replace('\\', '/');
        if (x.StartsWith("/", StringComparison.Ordinal) || x.Contains(":", StringComparison.Ordinal)) return string.Empty;

        var segs = x.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segs.Length == 0) return string.Empty;

        var outSegs = new List<string>(segs.Length);
        foreach (var seg in segs)
        {
            if (string.IsNullOrWhiteSpace(seg) || seg == ".") continue;
            if (seg == "..")
            {
                if (outSegs.Count > 0) outSegs.RemoveAt(outSegs.Count - 1);
                continue;
            }
            outSegs.Add(seg);
        }

        return outSegs.Count == 0 ? string.Empty : string.Join('/', outSegs);
    }

    private static string GuessLanguage(string p)
    {
        var ext = Path.GetExtension(p).ToLowerInvariant();
        return ext switch
        {
            ".js" => "javascript",
            ".jsx" => "javascript",
            ".ts" => "typescript",
            ".tsx" => "typescript",
            ".css" => "css",
            ".json" => "json",
            ".html" => "html",
            _ => "plaintext"
        };
    }

    private static JsonSerializerOptions JsonOpts() => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = JavaScriptEncoder.Default, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private static void Add(IDbCommand c, string n, object? v) { var p = c.CreateParameter(); p.ParameterName = n; p.Value = v ?? DBNull.Value; c.Parameters.Add(p); }
    private static void AddCompany(IDbCommand c, string n, Guid? id, bool sqlite) => Add(c, n, id.HasValue ? (sqlite ? id.Value.ToString() : id.Value) : null);

    private JsonResult UnauthorizedJson() => new(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
    private JsonResult ForbiddenJson(string m) => new(new { success = false, message = m }) { StatusCode = 403 };
    private JsonResult BadJson(string m) => new(new { success = false, message = m }) { StatusCode = 400 };
    private JsonResult NotFoundJson(string m) => new(new { success = false, message = m }) { StatusCode = 404 };

    private sealed class AccessContext { public required ApplicationUser User { get; init; } public required string OwnerUserId { get; init; } public Guid? CompanyId { get; init; } public bool CanManage { get; init; } }
    private sealed class BuilderRow { public int Id { get; set; } public string Name { get; set; } = string.Empty; public string EntryFilePath { get; set; } = "App.jsx"; }

    public class FileRow { public int Id { get; set; } public string Path { get; set; } = string.Empty; public string NodeType { get; set; } = "file"; public string Language { get; set; } = "plaintext"; public string? Content { get; set; } public int SortOrder { get; set; } public bool IsReadOnly { get; set; } public DateTime UpdatedAtUtc { get; set; } }

    public class SaveFilePayload { public int ReactBuilderId { get; set; } [Required] public string? Path { get; set; } public string? Content { get; set; } public string? Language { get; set; } }
    public class SaveWorkspacePayload { public int ReactBuilderId { get; set; } public List<WorkspaceNode>? Files { get; set; } }
    public class WorkspaceNode { public string Path { get; set; } = string.Empty; public string NodeType { get; set; } = "file"; public string? Language { get; set; } public string? Content { get; set; } public int SortOrder { get; set; } public bool IsReadOnly { get; set; } }
    public class CreateNodePayload { public int ReactBuilderId { get; set; } public string? ParentPath { get; set; } public string? Name { get; set; } public string? NodeType { get; set; } }
    public class RenameNodePayload { public int ReactBuilderId { get; set; } public string? Path { get; set; } public string? NewPath { get; set; } }
    public class DeleteNodePayload { public int ReactBuilderId { get; set; } public string? Path { get; set; } }
}

