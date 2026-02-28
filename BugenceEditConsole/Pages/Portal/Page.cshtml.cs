using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Portal;

public class PortalPageModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RepeaterTemplateService _repeaterService;
    private readonly DebugPanelLogService _debugLogService;

    public PortalPageModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, RepeaterTemplateService repeaterService, DebugPanelLogService debugLogService)
    {
        _db = db;
        _userManager = userManager;
        _repeaterService = repeaterService;
        _debugLogService = debugLogService;
    }

    public int PageId { get; private set; }
    public string PageTitle { get; private set; } = "Portal Page";
    public string? RenderedHtml { get; private set; }
    public bool AdminViewEnabled { get; private set; }
    public List<MasterpageOption> Masterpages { get; private set; } = new();
    public List<TemplateViewerOption> TemplateViewers { get; private set; } = new();
    public int? SelectedMasterpageId { get; private set; }
    public int? SelectedTemplateViewerId { get; private set; }
    public List<PagePortletRow> Portlets { get; private set; } = new();
    public string PortletsJson { get; private set; } = "[]";
    public string ReactSlotPayloadJson { get; private set; } = "{}";
    public bool HasReactSlots { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? id, string? slug, int? adminview)
    {
        PageId = id ?? 0;
        await EnsurePagesTableAsync();
        await EnsurePagePortletsTableAsync();
        await EnsureReactBuilderTablesAsync();

        var page = id.HasValue ? await LoadPageAsync(id.Value) : await LoadPageBySlugAsync(slug);
        if (page == null)
        {
            RenderedHtml = string.Empty;
            return Page();
        }

        PageTitle = string.IsNullOrWhiteSpace(page.Name) ? $"Page {id}" : page.Name;
        PageId = page.Id;
        SelectedMasterpageId = page.MasterpageId;
        SelectedTemplateViewerId = page.TemplateViewerId;

        var context = await GetAccessContextAsync();
        AdminViewEnabled = adminview == 1 && context != null && context.CanManage;

        if (AdminViewEnabled)
        {
            await EnsureMasterpagesTableAsync();
            await EnsureTempleteViewerTableAsync();
            Masterpages = await LoadMasterpagesAsync(page.OwnerUserId);
            TemplateViewers = await LoadTemplateViewersAsync(page.OwnerUserId);
            Portlets = await LoadPortletsAsync(page.OwnerUserId, page.Id);
            PortletsJson = System.Text.Json.JsonSerializer.Serialize(Portlets, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
        }

        if (page.MasterpageId.HasValue)
        {
            var masterpageHtml = await LoadMasterpageHtmlAsync(page.MasterpageId.Value);
            if (!string.IsNullOrWhiteSpace(masterpageHtml))
            {
                RenderedHtml = await RenderZonesAsync(masterpageHtml, page.OwnerUserId, page.Id);
            }
            else
            {
                RenderedHtml = string.Empty;
            }
        }
        else
        {
            RenderedHtml = string.Empty;
        }

        var slots = await LoadReactSlotsAsync(page.OwnerUserId, page.Id);
        ReactSlotPayloadJson = System.Text.Json.JsonSerializer.Serialize(slots, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
        HasReactSlots = slots.Count > 0;

        return Page();
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
            return new JsonResult(new { success = false, message = "You do not have permission to update pages." }) { StatusCode = 403 };
        }

        if (payload.PageId <= 0 || payload.MasterpageId <= 0)
        {
            return new JsonResult(new { success = false, message = "Invalid layout selection." }) { StatusCode = 400 };
        }

        await EnsurePagesTableAsync();
        var page = await LoadPageAsync(payload.PageId);
        if (page == null)
        {
            return new JsonResult(new { success = false, message = "Page not found." }) { StatusCode = 404 };
        }
        if (string.IsNullOrWhiteSpace(page.OwnerUserId))
        {
            return new JsonResult(new { success = false, message = "Unauthorized page update." }) { StatusCode = 403 };
        }
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"UPDATE Pages
SET MasterpageId = @master, UpdatedAtUtc = @updated
WHERE Id = @id AND OwnerUserId = @owner";
        AddParameter(command, "@master", payload.MasterpageId);
        AddParameter(command, "@updated", DateTime.UtcNow);
        AddParameter(command, "@id", payload.PageId);
        AddParameter(command, "@owner", page.OwnerUserId);
        var updated = await command.ExecuteNonQueryAsync();

        if (updated == 0)
        {
            return new JsonResult(new { success = false, message = "Unable to update page layout." }) { StatusCode = 400 };
        }

        return new JsonResult(new { success = true, message = "Layout updated." });
    }

    public async Task<IActionResult> OnPostAddPortletAsync([FromBody] PortletPayload payload)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }
        if (!context.CanManage)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to update pages." }) { StatusCode = 403 };
        }
        if (payload.PageId <= 0 || payload.TemplateViewerId <= 0 || string.IsNullOrWhiteSpace(payload.ZoneKey))
        {
            return new JsonResult(new { success = false, message = "Invalid portlet payload." }) { StatusCode = 400 };
        }

        await EnsurePagePortletsTableAsync();
        var page = await LoadPageAsync(payload.PageId);
        if (page == null)
        {
            return new JsonResult(new { success = false, message = "Page not found." }) { StatusCode = 404 };
        }
        if (string.IsNullOrWhiteSpace(page.OwnerUserId))
        {
            return new JsonResult(new { success = false, message = "Unauthorized page update." }) { StatusCode = 403 };
        }
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        if (payload.MasterpageId.HasValue && payload.MasterpageId.Value > 0)
        {
            await using var setMasterpage = connection.CreateCommand();
            setMasterpage.CommandText = @"UPDATE Pages
SET MasterpageId = @master, UpdatedAtUtc = @updated
WHERE Id = @id AND OwnerUserId = @owner AND (MasterpageId IS NULL OR MasterpageId <> @master)";
            AddParameter(setMasterpage, "@master", payload.MasterpageId.Value);
            AddParameter(setMasterpage, "@updated", DateTime.UtcNow);
            AddParameter(setMasterpage, "@id", payload.PageId);
            AddParameter(setMasterpage, "@owner", page.OwnerUserId);
            await setMasterpage.ExecuteNonQueryAsync();
        }

        var provider = _db.Database.ProviderName ?? string.Empty;
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        var dguid = Guid.NewGuid();
        await using var command = connection.CreateCommand();
        command.CommandText = @"INSERT INTO PagePortlets (OwnerUserId, PageId, DGUID, ZoneKey, TemplateViewerId, SortOrder, CreatedAtUtc, UpdatedAtUtc)
VALUES (@owner, @page, @dguid, @zone, @viewer, @order, @created, @updated)";
        AddParameter(command, "@owner", page.OwnerUserId);
        AddParameter(command, "@page", payload.PageId);
        AddParameter(command, "@dguid", isSqlite ? dguid.ToString("N") : dguid);
        AddParameter(command, "@zone", payload.ZoneKey.Trim());
        AddParameter(command, "@viewer", payload.TemplateViewerId);
        AddParameter(command, "@order", payload.SortOrder);
        AddParameter(command, "@created", DateTime.UtcNow);
        AddParameter(command, "@updated", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync();
        return new JsonResult(new { success = true, message = "Portlet added." });
    }

    public async Task<IActionResult> OnPostUpdatePortletAsync([FromBody] PortletUpdatePayload payload)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }
        if (!context.CanManage)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to update pages." }) { StatusCode = 403 };
        }
        if (payload.Id <= 0 || payload.PageId <= 0 || payload.TemplateViewerId <= 0 || string.IsNullOrWhiteSpace(payload.ZoneKey))
        {
            return new JsonResult(new { success = false, message = "Invalid portlet payload." }) { StatusCode = 400 };
        }

        await EnsurePagePortletsTableAsync();
        var page = await LoadPageAsync(payload.PageId);
        if (page == null)
        {
            return new JsonResult(new { success = false, message = "Page not found." }) { StatusCode = 404 };
        }
        if (string.IsNullOrWhiteSpace(page.OwnerUserId))
        {
            return new JsonResult(new { success = false, message = "Unauthorized page update." }) { StatusCode = 403 };
        }
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"UPDATE PagePortlets
SET ZoneKey = @zone, TemplateViewerId = @viewer, SortOrder = @order, UpdatedAtUtc = @updated
WHERE Id = @id AND PageId = @page AND OwnerUserId = @owner";
        AddParameter(command, "@zone", payload.ZoneKey.Trim());
        AddParameter(command, "@viewer", payload.TemplateViewerId);
        AddParameter(command, "@order", payload.SortOrder);
        AddParameter(command, "@updated", DateTime.UtcNow);
        AddParameter(command, "@id", payload.Id);
        AddParameter(command, "@page", payload.PageId);
        AddParameter(command, "@owner", page.OwnerUserId);
        var updated = await command.ExecuteNonQueryAsync();
        if (updated == 0)
        {
            return new JsonResult(new { success = false, message = "Unable to update portlet." }) { StatusCode = 400 };
        }
        return new JsonResult(new { success = true, message = "Portlet updated." });
    }

    public async Task<IActionResult> OnPostDeletePortletAsync([FromBody] DeletePortletPayload payload)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }
        if (!context.CanManage)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to update pages." }) { StatusCode = 403 };
        }
        if (payload.Id <= 0)
        {
            return new JsonResult(new { success = false, message = "Invalid portlet." }) { StatusCode = 400 };
        }

        await EnsurePagePortletsTableAsync();
        var page = await LoadPageAsync(payload.PageId);
        if (page == null)
        {
            return new JsonResult(new { success = false, message = "Page not found." }) { StatusCode = 404 };
        }
        if (string.IsNullOrWhiteSpace(page.OwnerUserId))
        {
            return new JsonResult(new { success = false, message = "Unauthorized page update." }) { StatusCode = 403 };
        }
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM PagePortlets WHERE Id = @id AND PageId = @page AND OwnerUserId = @owner";
        AddParameter(command, "@id", payload.Id);
        AddParameter(command, "@page", payload.PageId);
        AddParameter(command, "@owner", page.OwnerUserId);
        await command.ExecuteNonQueryAsync();
        return new JsonResult(new { success = true, message = "Portlet deleted." });
    }

    public async Task<IActionResult> OnGetTemplateViewerAsync(int id)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }
        if (!context.CanManage)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to edit template viewers." }) { StatusCode = 403 };
        }

        await EnsureTempleteViewerTableAsync();
        var page = await ResolveCurrentPageAsync();
        if (page == null)
        {
            return new JsonResult(new { success = false, message = "Page not found." }) { StatusCode = 404 };
        }

        var viewer = await LoadTemplateViewerRecordAsync(id, page.OwnerUserId);
        if (viewer == null)
        {
            return new JsonResult(new { success = false, message = "Template viewer not found." }) { StatusCode = 404 };
        }

        return new JsonResult(new
        {
            success = true,
            viewer = new
            {
                id = viewer.Id,
                name = viewer.Name,
                viewerType = viewer.ViewerType,
                templateText = viewer.TemplateText,
                dguid = viewer.Dguid
            }
        });
    }

    public async Task<IActionResult> OnPostUpdateTemplateViewerAsync([FromBody] UpdateTemplateViewerPayload payload)
    {
        var context = await GetAccessContextAsync();
        if (context == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }
        if (!context.CanManage)
        {
            return new JsonResult(new { success = false, message = "You do not have permission to edit template viewers." }) { StatusCode = 403 };
        }
        if (payload.Id <= 0 || string.IsNullOrWhiteSpace(payload.Name) || string.IsNullOrWhiteSpace(payload.TemplateText))
        {
            return new JsonResult(new { success = false, message = "Please provide template name and content." }) { StatusCode = 400 };
        }

        await EnsureTempleteViewerTableAsync();
        var page = await ResolveCurrentPageAsync();
        if (page == null)
        {
            return new JsonResult(new { success = false, message = "Page not found." }) { StatusCode = 404 };
        }

        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"UPDATE TempleteViewers
SET Name = @name, ViewerType = @type, TemplateText = @text, UpdatedAtUtc = @updated
WHERE Id = @id AND OwnerUserId = @owner";
        AddParameter(command, "@name", payload.Name.Trim());
        AddParameter(command, "@type", string.IsNullOrWhiteSpace(payload.ViewerType) ? "Page Templete Viewer" : payload.ViewerType.Trim());
        AddParameter(command, "@text", payload.TemplateText);
        AddParameter(command, "@updated", DateTime.UtcNow);
        AddParameter(command, "@id", payload.Id);
        AddParameter(command, "@owner", page.OwnerUserId);
        var updated = await command.ExecuteNonQueryAsync();
        if (updated == 0)
        {
            return new JsonResult(new { success = false, message = "Unable to update template viewer." }) { StatusCode = 400 };
        }

        return new JsonResult(new { success = true, message = "Template viewer updated." });
    }

    public async Task<IActionResult> OnGetZonesAsync(int masterpageId)
    {
        var html = await LoadMasterpageHtmlAsync(masterpageId);
        var zones = ExtractZones(html ?? string.Empty);
        return new JsonResult(new { success = true, zones });
    }

    private async Task<string> RenderZonesAsync(string masterpageHtml, string ownerUserId, int pageId)
    {
        var zones = ExtractZones(masterpageHtml);
        if (zones.Count == 0)
        {
            return await _repeaterService.RenderAsync(masterpageHtml, ownerUserId);
        }

        var portlets = await LoadPortletsAsync(ownerUserId, pageId);
        var html = masterpageHtml;
        foreach (var zone in zones)
        {
            var zonePortlets = portlets
                .Where(p => string.Equals(p.ZoneKey, zone, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.SortOrder)
                .ToList();
            var zoneContent = new System.Text.StringBuilder();
            foreach (var portlet in zonePortlets)
            {
                try
                {
                    var viewerHtml = await LoadTemplateViewerHtmlAsync(portlet.TemplateViewerId);
                    if (string.IsNullOrWhiteSpace(viewerHtml))
                    {
                        continue;
                    }
                    var rendered = await _repeaterService.RenderAsync(viewerHtml, ownerUserId);
                    zoneContent.Append(rendered);
                }
                catch (Exception ex)
                {
                    await _debugLogService.LogErrorAsync(
                        source: "Portal.Page.PortletRender",
                        shortDescription: ex.Message,
                        longDescription: ex.ToString(),
                        ownerUserId: ownerUserId,
                        path: $"PageId={pageId}; Zone={zone}; PortletId={portlet.Id}");
                }
            }
            var token = $"<Masterpage-{zone}>";
            html = Regex.Replace(html, Regex.Escape(token), zoneContent.ToString(), RegexOptions.IgnoreCase);
        }

        foreach (var zone in zones)
        {
            var token = $"<Masterpage-{zone}>";
            html = Regex.Replace(html, Regex.Escape(token), string.Empty, RegexOptions.IgnoreCase);
        }

        return await _repeaterService.RenderAsync(html, ownerUserId);
    }

    private async Task<PageRecord?> LoadPageAsync(int id)
    {
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, OwnerUserId, Name, MasterpageId, TemplateViewerId, Slug FROM Pages WHERE Id = @id";
        AddParameter(command, "@id", id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PageRecord
        {
            Id = reader.GetInt32(0),
            OwnerUserId = reader.GetString(1),
            Name = reader.GetString(2),
            MasterpageId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            TemplateViewerId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            Slug = reader.IsDBNull(5) ? string.Empty : reader.GetString(5)
        };
    }

    private async Task<PageRecord?> LoadPageBySlugAsync(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, OwnerUserId, Name, MasterpageId, TemplateViewerId, Slug FROM Pages WHERE Slug = @slug";
        AddParameter(command, "@slug", slug.Trim());
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new PageRecord
        {
            Id = reader.GetInt32(0),
            OwnerUserId = reader.GetString(1),
            Name = reader.GetString(2),
            MasterpageId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            TemplateViewerId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            Slug = reader.IsDBNull(5) ? string.Empty : reader.GetString(5)
        };
    }

    private async Task<PageRecord?> ResolveCurrentPageAsync()
    {
        var routeValue = RouteData.Values["id"]?.ToString();
        if (!int.TryParse(routeValue, out var pageId) || pageId <= 0)
        {
            return null;
        }

        return await LoadPageAsync(pageId);
    }

    private async Task<string?> LoadMasterpageHtmlAsync(int id)
    {
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MasterpageText FROM Masterpages WHERE Id = @id";
        AddParameter(command, "@id", id);
        var result = await command.ExecuteScalarAsync();
        return result == null || result is DBNull ? null : result.ToString();
    }

    private async Task<string?> LoadTemplateViewerHtmlAsync(int id)
    {
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TemplateText FROM TempleteViewers WHERE Id = @id";
        AddParameter(command, "@id", id);
        var result = await command.ExecuteScalarAsync();
        return result == null || result is DBNull ? null : result.ToString();
    }

    private async Task<TemplateViewerRecord?> LoadTemplateViewerRecordAsync(int id, string ownerUserId)
    {
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT Id, DGUID, Name, ViewerType, TemplateText
FROM TempleteViewers
WHERE Id = @id AND OwnerUserId = @owner";
        AddParameter(command, "@id", id);
        AddParameter(command, "@owner", ownerUserId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new TemplateViewerRecord
        {
            Id = reader.GetInt32(0),
            Dguid = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            Name = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            ViewerType = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            TemplateText = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
        };
    }

    private async Task<List<MasterpageOption>> LoadMasterpagesAsync(string ownerUserId)
    {
        var list = new List<MasterpageOption>();
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name FROM Masterpages WHERE OwnerUserId = @owner ORDER BY Name";
        AddParameter(command, "@owner", ownerUserId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new MasterpageOption
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1)
            });
        }

        return list;
    }

    private async Task<List<TemplateViewerOption>> LoadTemplateViewersAsync(string ownerUserId)
    {
        var list = new List<TemplateViewerOption>();
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name FROM TempleteViewers WHERE OwnerUserId = @owner ORDER BY Name";
        AddParameter(command, "@owner", ownerUserId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new TemplateViewerOption
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1)
            });
        }

        return list;
    }

    private async Task<List<PagePortletRow>> LoadPortletsAsync(string ownerUserId, int pageId)
    {
        var list = new List<PagePortletRow>();
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT p.Id, p.ZoneKey, p.TemplateViewerId, p.SortOrder, v.Name
FROM PagePortlets p
LEFT JOIN TempleteViewers v ON v.Id = p.TemplateViewerId
WHERE p.OwnerUserId = @owner AND p.PageId = @page
ORDER BY p.SortOrder ASC, p.Id ASC";
        AddParameter(command, "@owner", ownerUserId);
        AddParameter(command, "@page", pageId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new PagePortletRow
            {
                Id = reader.GetInt32(0),
                ZoneKey = reader.GetString(1),
                TemplateViewerId = reader.GetInt32(2),
                SortOrder = reader.GetInt32(3),
                TemplateViewerName = reader.IsDBNull(4) ? "—" : reader.GetString(4)
            });
        }
        return list;
    }

    private async Task<Dictionary<string, ReactSlotWorkspace>> LoadReactSlotsAsync(string ownerUserId, int pageId)
    {
        var map = new Dictionary<string, ReactSlotWorkspace>(StringComparer.OrdinalIgnoreCase);
        using var connection = _db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT TopReactBuilderId, MenuReactBuilderId, BodyReactBuilderId, SubtemplateReactBuilderId
FROM ReactPageConnections
WHERE OwnerUserId = @owner AND PageId = @page";
        AddParameter(cmd, "@owner", ownerUserId);
        AddParameter(cmd, "@page", pageId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return map;
        }

        var slots = new (string Slot, int? BuilderId)[]
        {
            ("top", reader.IsDBNull(0) ? null : reader.GetInt32(0)),
            ("menu", reader.IsDBNull(1) ? null : reader.GetInt32(1)),
            ("body", reader.IsDBNull(2) ? null : reader.GetInt32(2)),
            ("subtemplate", reader.IsDBNull(3) ? null : reader.GetInt32(3))
        };
        await reader.CloseAsync();

        foreach (var slot in slots)
        {
            if (!slot.BuilderId.HasValue || slot.BuilderId.Value <= 0)
            {
                continue;
            }

            var workspace = await LoadReactWorkspaceAsync(connection, ownerUserId, slot.BuilderId.Value);
            if (workspace != null)
            {
                map[slot.Slot] = workspace;
            }
        }

        return map;
    }

    private async Task<ReactSlotWorkspace?> LoadReactWorkspaceAsync(DbConnection connection, string ownerUserId, int builderId)
    {
        await using var b = connection.CreateCommand();
        b.CommandText = @"SELECT Id, Name, EntryFilePath
FROM ReactBuilders
WHERE Id = @id AND OwnerUserId = @owner AND IsActive = 1";
        AddParameter(b, "@id", builderId);
        AddParameter(b, "@owner", ownerUserId);
        await using var br = await b.ExecuteReaderAsync();
        if (!await br.ReadAsync())
        {
            return null;
        }

        var workspace = new ReactSlotWorkspace
        {
            BuilderId = br.GetInt32(0),
            BuilderName = br.IsDBNull(1) ? "React Builder" : br.GetString(1),
            EntryFilePath = br.IsDBNull(2) ? "App.jsx" : br.GetString(2)
        };
        await br.CloseAsync();

        await using var f = connection.CreateCommand();
        f.CommandText = @"SELECT Path, NodeType, Language, Content
FROM ReactBuilderFiles
WHERE ReactBuilderId = @builder AND OwnerUserId = @owner
ORDER BY CASE WHEN NodeType = 'folder' THEN 0 ELSE 1 END, Path";
        AddParameter(f, "@builder", builderId);
        AddParameter(f, "@owner", ownerUserId);
        await using var fr = await f.ExecuteReaderAsync();
        while (await fr.ReadAsync())
        {
            workspace.Files.Add(new ReactSlotFile
            {
                Path = fr.IsDBNull(0) ? string.Empty : fr.GetString(0),
                NodeType = fr.IsDBNull(1) ? "file" : fr.GetString(1),
                Language = fr.IsDBNull(2) ? "plaintext" : fr.GetString(2),
                Content = fr.IsDBNull(3) ? string.Empty : fr.GetString(3)
            });
        }

        return workspace;
    }

    private static List<string> ExtractZones(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return new List<string>();
        }
        var zones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(html, @"<Masterpage-([A-Za-z0-9]+)>", RegexOptions.IgnoreCase))
        {
            if (match.Groups.Count > 1)
            {
                zones.Add(match.Groups[1].Value);
            }
        }
        return zones.ToList();
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
                var hasSlug = false;
                while (await reader.ReadAsync())
                {
                    var colName = reader.GetString(1);
                    if (string.Equals(colName, "Slug", StringComparison.OrdinalIgnoreCase))
                    {
                        hasSlug = true;
                        break;
                    }
                }
                if (!hasSlug)
                {
                    await _db.Database.ExecuteSqlRawAsync(@"ALTER TABLE Pages ADD COLUMN Slug TEXT NOT NULL DEFAULT '';");
                }
            }
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Pages' AND xtype='U')
CREATE TABLE Pages (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NOT NULL,
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
AND NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'Slug' AND Object_ID = Object_ID(N'Pages'))
ALTER TABLE Pages ADD Slug NVARCHAR(200) NOT NULL DEFAULT('');
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
    PageId INTEGER NOT NULL,
    DGUID TEXT NOT NULL,
    ZoneKey TEXT NOT NULL,
    TemplateViewerId INTEGER NOT NULL,
    SortOrder INTEGER NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);");
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='PagePortlets' AND xtype='U')
CREATE TABLE PagePortlets (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NOT NULL,
    PageId INT NOT NULL,
    DGUID UNIQUEIDENTIFIER NOT NULL,
    ZoneKey NVARCHAR(100) NOT NULL,
    TemplateViewerId INT NOT NULL,
    SortOrder INT NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL
);");
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
    DGUID TEXT NOT NULL,
    Name TEXT NOT NULL,
    TemplateViewerId INTEGER NOT NULL,
    MasterpageText TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);");
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Masterpages' AND xtype='U')
CREATE TABLE Masterpages (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NOT NULL,
    DGUID UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    TemplateViewerId INT NOT NULL,
    MasterpageText NVARCHAR(MAX) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL
);");
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
    DGUID TEXT NOT NULL,
    Name TEXT NOT NULL,
    ViewerType TEXT NOT NULL,
    TemplateText TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);");
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='TempleteViewers' AND xtype='U')
CREATE TABLE TempleteViewers (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NOT NULL,
    DGUID UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    ViewerType NVARCHAR(200) NOT NULL,
    TemplateText NVARCHAR(MAX) NOT NULL,
    CreatedAtUtc DATETIME2 NOT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL
);");
    }

    private async Task EnsureReactBuilderTablesAsync()
    {
        var provider = _db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ReactBuilders (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerUserId TEXT NOT NULL,
    CompanyId TEXT NULL,
    DGUID TEXT NOT NULL,
    Name TEXT NOT NULL,
    Description TEXT NULL,
    EntryFilePath TEXT NOT NULL DEFAULT 'App.jsx',
    PreviewMasterpageId INTEGER NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);");
            await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ReactBuilderFiles (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ReactBuilderId INTEGER NOT NULL,
    OwnerUserId TEXT NOT NULL,
    CompanyId TEXT NULL,
    DGUID TEXT NOT NULL,
    Path TEXT NOT NULL,
    NodeType TEXT NOT NULL,
    Language TEXT NOT NULL,
    Content TEXT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    IsReadOnly INTEGER NOT NULL DEFAULT 0,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);");
            await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ReactPageConnections (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OwnerUserId TEXT NOT NULL,
    CompanyId TEXT NULL,
    PageId INTEGER NOT NULL,
    TopReactBuilderId INTEGER NULL,
    MenuReactBuilderId INTEGER NULL,
    BodyReactBuilderId INTEGER NULL,
    SubtemplateReactBuilderId INTEGER NULL,
    UpdatedAtUtc TEXT NOT NULL
);");
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ReactBuilders' AND xtype='U')
CREATE TABLE ReactBuilders (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NOT NULL,
    CompanyId UNIQUEIDENTIFIER NULL,
    DGUID UNIQUEIDENTIFIER NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    EntryFilePath NVARCHAR(400) NOT NULL DEFAULT('App.jsx'),
    PreviewMasterpageId INT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAtUtc DATETIME2 NOT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL
);");
        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ReactBuilderFiles' AND xtype='U')
CREATE TABLE ReactBuilderFiles (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ReactBuilderId INT NOT NULL,
    OwnerUserId NVARCHAR(450) NOT NULL,
    CompanyId UNIQUEIDENTIFIER NULL,
    DGUID UNIQUEIDENTIFIER NOT NULL,
    Path NVARCHAR(600) NOT NULL,
    NodeType NVARCHAR(20) NOT NULL,
    Language NVARCHAR(60) NOT NULL,
    Content NVARCHAR(MAX) NULL,
    SortOrder INT NOT NULL DEFAULT 0,
    IsReadOnly BIT NOT NULL DEFAULT 0,
    CreatedAtUtc DATETIME2 NOT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL
);");
        await _db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='ReactPageConnections' AND xtype='U')
CREATE TABLE ReactPageConnections (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OwnerUserId NVARCHAR(450) NOT NULL,
    CompanyId UNIQUEIDENTIFIER NULL,
    PageId INT NOT NULL,
    TopReactBuilderId INT NULL,
    MenuReactBuilderId INT NULL,
    BodyReactBuilderId INT NULL,
    SubtemplateReactBuilderId INT NULL,
    UpdatedAtUtc DATETIME2 NOT NULL
);");
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

    private sealed class AccessContext
    {
        public required ApplicationUser User { get; init; }
        public required string OwnerUserId { get; init; }
        public bool CanManage { get; init; }
    }

    private sealed class PageRecord
    {
        public int Id { get; set; }
        public string OwnerUserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int? MasterpageId { get; set; }
        public int? TemplateViewerId { get; set; }
        public string Slug { get; set; } = string.Empty;
    }

    private sealed class TemplateViewerRecord
    {
        public int Id { get; set; }
        public string Dguid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ViewerType { get; set; } = string.Empty;
        public string TemplateText { get; set; } = string.Empty;
    }

    public class MasterpageOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class TemplateViewerOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class UpdateMasterpagePayload
    {
        public int PageId { get; set; }

        [Required]
        public int MasterpageId { get; set; }
    }

    public class PortletPayload
    {
        public int PageId { get; set; }
        public int? MasterpageId { get; set; }
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
        public int PageId { get; set; }
    }

    public class UpdateTemplateViewerPayload
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ViewerType { get; set; } = string.Empty;
        public string TemplateText { get; set; } = string.Empty;
    }

    public class PagePortletRow
    {
        public int Id { get; set; }
        public string ZoneKey { get; set; } = string.Empty;
        public int TemplateViewerId { get; set; }
        public string TemplateViewerName { get; set; } = string.Empty;
        public int SortOrder { get; set; }
    }

    public class ReactSlotWorkspace
    {
        public int BuilderId { get; set; }
        public string BuilderName { get; set; } = "React Builder";
        public string EntryFilePath { get; set; } = "App.jsx";
        public List<ReactSlotFile> Files { get; set; } = new();
    }

    public class ReactSlotFile
    {
        public string Path { get; set; } = string.Empty;
        public string NodeType { get; set; } = "file";
        public string Language { get; set; } = "plaintext";
        public string Content { get; set; } = string.Empty;
    }
}
