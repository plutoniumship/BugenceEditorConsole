using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Workflows;

[Authorize]
public class EditorModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISensitiveDataProtector _protector;

    public EditorModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager, ISensitiveDataProtector protector)
    {
        _db = db;
        _userManager = userManager;
        _protector = protector;
    }

    public Workflow Workflow { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        await EnsureWorkflowSchemaAsync();
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return RedirectToPage("/Auth/Login");
        }

        var workflow = await _db.Workflows.FirstOrDefaultAsync(w => w.Id == id
            && w.OwnerUserId == user.Id
            && (user.CompanyId.HasValue ? w.CompanyId == user.CompanyId : w.CompanyId == null));
        if (workflow == null)
        {
            return RedirectToPage("/Workflows/Index");
        }

        Workflow = workflow;
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync([FromBody] SaveWorkflowPayload payload)
    {
        await EnsureWorkflowSchemaAsync();
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in." }) { StatusCode = 401 };
        }

        if (!ModelState.IsValid)
        {
            return new JsonResult(new { success = false, message = "Invalid workflow data." }) { StatusCode = 400 };
        }

        var workflow = await _db.Workflows.FirstOrDefaultAsync(w => w.Id == payload.Id
            && w.OwnerUserId == user.Id
            && (user.CompanyId.HasValue ? w.CompanyId == user.CompanyId : w.CompanyId == null));
        if (workflow == null)
        {
            return new JsonResult(new { success = false, message = "Workflow not found." }) { StatusCode = 404 };
        }

        workflow.Name = payload.Name.Trim();
        workflow.Caption = string.IsNullOrWhiteSpace(payload.Caption) ? payload.Name.Trim() : payload.Caption.Trim();
        workflow.Description = string.IsNullOrWhiteSpace(payload.Description) ? null : payload.Description.Trim();
        workflow.Status = payload.Status;
        workflow.WorkflowType = string.IsNullOrWhiteSpace(payload.WorkflowType) ? "Application Workflow" : payload.WorkflowType.Trim();
        workflow.ApplicationId = string.IsNullOrWhiteSpace(payload.ApplicationId) ? null : payload.ApplicationId.Trim();
        workflow.FileListId = payload.FileListId;
        workflow.ViewOnApplication = payload.ViewOnApplication;
        workflow.StartupType = payload.StartupType;
        workflow.StartupArgument1 = string.IsNullOrWhiteSpace(payload.StartupArgument1) ? null : payload.StartupArgument1.Trim();
        workflow.StartupArgument2 = string.IsNullOrWhiteSpace(payload.StartupArgument2) ? null : payload.StartupArgument2.Trim();
        workflow.InActive = payload.InActive;
        workflow.Diagram = string.IsNullOrWhiteSpace(payload.Diagram) ? null : payload.Diagram.Trim();
        workflow.KpiActivity = string.IsNullOrWhiteSpace(payload.KpiActivity) ? null : payload.KpiActivity.Trim();
        workflow.DefinitionJson = payload.DefinitionJson;
        workflow.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return new JsonResult(new { success = true, updatedAt = workflow.UpdatedAtUtc });
    }

    public async Task<IActionResult> OnGetLogsAsync(Guid id)
    {
        await EnsureWorkflowSchemaAsync();
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { logs = Array.Empty<object>() });
        }

        var workflow = await _db.Workflows.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id
            && w.OwnerUserId == user.Id
            && (user.CompanyId.HasValue ? w.CompanyId == user.CompanyId : w.CompanyId == null));
        if (workflow == null)
        {
            return new JsonResult(new { logs = Array.Empty<object>() });
        }

        var logs = await _db.WorkflowExecutionLogs
            .AsNoTracking()
            .Where(l => l.WorkflowId == id)
            .OrderByDescending(l => l.ExecutedAtUtc)
            .Take(8)
            .Select(l => new
            {
                l.Status,
                l.Message,
                l.StepName,
                l.ExecutedAtUtc
            })
            .ToListAsync();

        return new JsonResult(new { logs });
    }

    public async Task<IActionResult> OnGetLogStreamAsync(Guid id)
    {
        await EnsureWorkflowSchemaAsync();
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new EmptyResult();
        }

        var workflow = await _db.Workflows.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id
            && w.OwnerUserId == user.Id
            && (user.CompanyId.HasValue ? w.CompanyId == user.CompanyId : w.CompanyId == null));
        if (workflow == null)
        {
            return new EmptyResult();
        }

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var lastSent = DateTime.MinValue;
        while (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            var newLogs = await _db.WorkflowExecutionLogs
                .AsNoTracking()
                .Where(l => l.WorkflowId == id && l.ExecutedAtUtc > lastSent)
                .OrderBy(l => l.ExecutedAtUtc)
                .Take(5)
                .Select(l => new
                {
                    l.Status,
                    l.Message,
                    l.StepName,
                    l.ExecutedAtUtc
                })
                .ToListAsync();

            foreach (var entry in newLogs)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(entry);
                await Response.WriteAsync($"data: {json}\n\n");
                await Response.Body.FlushAsync();
                if (entry.ExecutedAtUtc > lastSent)
                {
                    lastSent = entry.ExecutedAtUtc;
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), HttpContext.RequestAborted);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        return new EmptyResult();
    }

    public async Task<IActionResult> OnGetSmtpProfilesAsync(Guid id)
    {
        await EnsureWorkflowSchemaAsync();
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { profiles = Array.Empty<object>() });
        }

        var workflow = await _db.Workflows.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id
            && w.OwnerUserId == user.Id
            && (user.CompanyId.HasValue ? w.CompanyId == user.CompanyId : w.CompanyId == null));
        if (workflow == null)
        {
            return new JsonResult(new { profiles = Array.Empty<object>() });
        }

        var scope = await CompanyAccessScopeResolver.ResolveAsync(_db, user);
        var profiles = await SystemPropertySmtpLoader.LoadProfilesAsync(
            _db,
            _protector,
            scope.OwnerUserId,
            scope.CompanyId,
            HttpContext.RequestAborted);

        return new JsonResult(new
        {
            profiles = profiles.Select(p => new
            {
                id = p.Id,
                dguid = p.Dguid,
                name = p.Name,
                host = p.Host,
                port = p.Port,
                fromAddress = p.FromAddress,
                fromName = p.FromName
            })
        });
    }

    public async Task<IActionResult> OnGetImportFormTokensAsync(Guid id)
    {
        await EnsureWorkflowSchemaAsync();
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in." }) { StatusCode = 401 };
        }

        var workflow = await _db.Workflows.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id
            && w.OwnerUserId == user.Id
            && (user.CompanyId.HasValue ? w.CompanyId == user.CompanyId : w.CompanyId == null));
        if (workflow == null)
        {
            return new JsonResult(new { success = false, message = "Workflow not found." }) { StatusCode = 404 };
        }

        var webHostEnvironment = HttpContext.RequestServices.GetService(typeof(IWebHostEnvironment)) as IWebHostEnvironment;
        var webRoot = webHostEnvironment?.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            return new JsonResult(new { success = true, tokens = Array.Empty<object>(), message = "Web root not available." });
        }

        var projects = await _db.UploadedProjects
            .AsNoTracking()
            .Where(p => p.UserId == user.Id && (user.CompanyId.HasValue ? p.CompanyId == user.CompanyId : p.CompanyId == null))
            .OrderByDescending(p => p.UploadedAtUtc)
            .Take(50)
            .Select(p => new { p.Id, Name = (p.DisplayName ?? p.Slug), p.FolderName })
            .ToListAsync();

        var tokenSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokenRows = new List<ImportedTokenRow>();
        var matchedWorkflowTaggedForm = false;
        var idMarker = workflow.Id.ToString();
        var dguidMarker = workflow.Dguid?.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant() ?? string.Empty;
        var attrRegex = new Regex("data-bugence-workflow-(?:id|dguid)\\s*=\\s*(['\\\"])(.*?)\\1", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var fieldRegex = new Regex("<(?:input|select|textarea)\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (var project in projects)
        {
            if (string.IsNullOrWhiteSpace(project.FolderName)) continue;
            var projectRoot = Path.Combine(webRoot, "Uploads", project.FolderName);
            if (!Directory.Exists(projectRoot)) continue;

            var htmlFiles = Directory.EnumerateFiles(projectRoot, "*.htm*", SearchOption.AllDirectories).Take(300);
            foreach (var filePath in htmlFiles)
            {
                string html;
                try
                {
                    html = await System.IO.File.ReadAllTextAsync(filePath, HttpContext.RequestAborted);
                }
                catch
                {
                    continue;
                }

                foreach (Match attrMatch in attrRegex.Matches(html))
                {
                    var value = (attrMatch.Groups[2].Value ?? string.Empty).Trim();
                    var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();
                    var isWorkflowMatch =
                        value.Equals(idMarker, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(dguidMarker) && normalized.Equals(dguidMarker, StringComparison.OrdinalIgnoreCase));
                    if (!isWorkflowMatch)
                    {
                        continue;
                    }

                    var formHtml = TryExtractContainingForm(html, attrMatch.Index);
                    if (string.IsNullOrWhiteSpace(formHtml))
                    {
                        continue;
                    }

                    var added = AddTokensFromFormHtml(
                        formHtml,
                        fieldRegex,
                        tokenSet,
                        tokenRows,
                        project.Id,
                        project.Name,
                        projectRoot,
                        filePath,
                        "form-field");
                    if (added > 0)
                    {
                        matchedWorkflowTaggedForm = true;
                    }
                }
            }
        }

        // Fallback: if no workflow-tagged form was found yet, still surface available form tokens
        // from the same uploaded projects so users can continue email token mapping immediately.
        if (!matchedWorkflowTaggedForm && tokenRows.Count == 0)
        {
            foreach (var project in projects)
            {
                if (string.IsNullOrWhiteSpace(project.FolderName)) continue;
                var projectRoot = Path.Combine(webRoot, "Uploads", project.FolderName);
                if (!Directory.Exists(projectRoot)) continue;

                var htmlFiles = Directory.EnumerateFiles(projectRoot, "*.htm*", SearchOption.AllDirectories).Take(120);
                foreach (var filePath in htmlFiles)
                {
                    string html;
                    try
                    {
                        html = await System.IO.File.ReadAllTextAsync(filePath, HttpContext.RequestAborted);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (Match formMatch in Regex.Matches(html, "<form\\b[\\s\\S]*?</form>", RegexOptions.IgnoreCase))
                    {
                        AddTokensFromFormHtml(
                            formMatch.Value,
                            fieldRegex,
                            tokenSet,
                            tokenRows,
                            project.Id,
                            project.Name,
                            projectRoot,
                            filePath,
                            "fallback-form-field");
                    }
                }

                if (tokenRows.Count >= 40)
                {
                    break;
                }
            }
        }

        return new JsonResult(new
        {
            success = true,
            tokens = tokenRows.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase).Select(t => new
            {
                key = t.Key,
                token = t.Token,
                projectId = t.ProjectId,
                projectName = t.ProjectName,
                file = t.File,
                source = t.Source
            }),
            count = tokenRows.Count
        });
    }

    public class SaveWorkflowPayload
    {
        [Required]
        public Guid Id { get; set; }

        [Required, MinLength(2), MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(320)]
        public string? Description { get; set; }

        [MaxLength(180)]
        public string? Caption { get; set; }

        [Required]
        public string Status { get; set; } = "Draft";

        [MaxLength(80)]
        public string WorkflowType { get; set; } = "Application Workflow";

        [MaxLength(180)]
        public string? ApplicationId { get; set; }

        public int FileListId { get; set; }

        public int ViewOnApplication { get; set; }

        public int StartupType { get; set; }

        [MaxLength(280)]
        public string? StartupArgument1 { get; set; }

        [MaxLength(280)]
        public string? StartupArgument2 { get; set; }

        [MaxLength(512)]
        public string? Diagram { get; set; }

        [MaxLength(180)]
        public string? KpiActivity { get; set; }

        public bool InActive { get; set; }

        [Required]
        public string DefinitionJson { get; set; } = "{}";
    }

    private Task EnsureWorkflowSchemaAsync()
        => WorkflowSchemaService.EnsureLegacyColumnsAsync(_db);

    private static string? TryExtractContainingForm(string html, int markerIndex)
    {
        if (string.IsNullOrWhiteSpace(html) || markerIndex < 0 || markerIndex >= html.Length)
        {
            return null;
        }

        var formStart = html.LastIndexOf("<form", markerIndex, StringComparison.OrdinalIgnoreCase);
        if (formStart < 0)
        {
            return null;
        }

        var formEnd = html.IndexOf("</form>", formStart, StringComparison.OrdinalIgnoreCase);
        if (formEnd < 0 || markerIndex > formEnd)
        {
            return null;
        }

        var length = formEnd + "</form>".Length - formStart;
        if (length <= 0 || formStart + length > html.Length)
        {
            return null;
        }

        return html.Substring(formStart, length);
    }

    private static string? ExtractAttribute(string tag, string attributeName)
    {
        if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(attributeName))
        {
            return null;
        }

        var pattern = $"{Regex.Escape(attributeName)}\\s*=\\s*(['\\\"])(.*?)\\1";
        var match = Regex.Match(tag, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success) return null;
        return System.Net.WebUtility.HtmlDecode(match.Groups[2].Value);
    }

    private static string GetProjectRelativePath(string projectRoot, string fullPath)
    {
        try
        {
            var relative = Path.GetRelativePath(projectRoot, fullPath).Replace("\\", "/");
            return relative;
        }
        catch
        {
            return Path.GetFileName(fullPath);
        }
    }

    private static bool IsDetailsLikeField(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var normalized = new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return normalized is "details" or "message" or "inquiry" or "comment" or "description" or "body" or "text";
    }

    private static bool IsNameLikeField(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var normalized = new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return normalized is "name" or "fullname";
    }

    private static void AddAliasToken(
        HashSet<string> tokenSet,
        List<ImportedTokenRow> tokenRows,
        string key,
        int projectId,
        string projectName,
        string projectRoot,
        string filePath,
        string source)
    {
        if (!tokenSet.Add(key)) return;
        tokenRows.Add(new ImportedTokenRow
        {
            Key = key,
            Token = $"^{key}^",
            ProjectId = projectId,
            ProjectName = projectName,
            File = GetProjectRelativePath(projectRoot, filePath),
            Source = source
        });
    }

    private static int AddTokensFromFormHtml(
        string formHtml,
        Regex fieldRegex,
        HashSet<string> tokenSet,
        List<ImportedTokenRow> tokenRows,
        int projectId,
        string projectName,
        string projectRoot,
        string filePath,
        string source)
    {
        if (string.IsNullOrWhiteSpace(formHtml))
        {
            return 0;
        }

        var added = 0;
        foreach (Match fieldMatch in fieldRegex.Matches(formHtml))
        {
            var tag = fieldMatch.Value;
            var name = ExtractAttribute(tag, "name");
            var idValue = ExtractAttribute(tag, "id");

            var candidates = new[] { name, idValue }
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())
                .Where(v => !v.Equals("__RequestVerificationToken", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var candidate in candidates)
            {
                if (!tokenSet.Add(candidate)) continue;
                tokenRows.Add(new ImportedTokenRow
                {
                    Key = candidate,
                    Token = $"^{candidate}^",
                    ProjectId = projectId,
                    ProjectName = projectName,
                    File = GetProjectRelativePath(projectRoot, filePath),
                    Source = source
                });
                added++;

                if (IsDetailsLikeField(candidate))
                {
                    AddAliasToken(tokenSet, tokenRows, "details", projectId, projectName, projectRoot, filePath, "alias");
                    AddAliasToken(tokenSet, tokenRows, "message", projectId, projectName, projectRoot, filePath, "alias");
                }
                if (IsNameLikeField(candidate))
                {
                    AddAliasToken(tokenSet, tokenRows, "fullName", projectId, projectName, projectRoot, filePath, "alias");
                    AddAliasToken(tokenSet, tokenRows, "name", projectId, projectName, projectRoot, filePath, "alias");
                }
            }
        }

        return added;
    }

    private sealed class ImportedTokenRow
    {
        public string Key { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }
}
