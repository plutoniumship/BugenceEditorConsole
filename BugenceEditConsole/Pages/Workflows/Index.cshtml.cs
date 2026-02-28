using System.ComponentModel.DataAnnotations;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using BugenceEditConsole.Services;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Pages.Workflows;

[Authorize]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public List<WorkflowListItem> Workflows { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(bool archived = false)
    {
        await EnsureWorkflowSchemaAsync();
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return RedirectToPage("/Auth/Login");
        }

        IQueryable<Workflow> query = _db.Workflows
            .Where(w => w.OwnerUserId == user.Id);
        query = user.CompanyId.HasValue
            ? query.Where(w => w.CompanyId == user.CompanyId)
            : query.Where(w => w.CompanyId == null);

        query = archived
            ? query.Where(w => w.Status == "Archived")
            : query.Where(w => w.Status != "Archived" && w.Status != "Deleted");

        Workflows = await query
            .OrderByDescending(w => w.UpdatedAtUtc)
            .Select(w => new WorkflowListItem
            {
                Id = w.Id,
                DisplayId = w.DisplayId,
                Dguid = w.Dguid,
                Name = w.Name,
                Caption = w.Caption ?? w.Name,
                WorkflowType = w.WorkflowType,
                ApplicationId = w.ApplicationId,
                FileListId = w.FileListId,
                ViewOnApplication = w.ViewOnApplication,
                StartupType = w.StartupType,
                StartupArgument1 = w.StartupArgument1,
                StartupArgument2 = w.StartupArgument2,
                InActive = w.InActive,
                Diagram = w.Diagram,
                KpiActivity = w.KpiActivity,
                CreatedBy = w.CreatedByName,
                Status = w.Status,
                TriggerType = w.TriggerType,
                CreatedAtUtc = w.CreatedAtUtc,
                UpdatedAtUtc = w.UpdatedAtUtc
            })
            .ToListAsync();

        if (Workflows.Count == 0)
        {
            IQueryable<Workflow> fallbackQuery = _db.Workflows;
            fallbackQuery = archived
                ? fallbackQuery.Where(w => w.Status == "Archived")
                : fallbackQuery.Where(w => w.Status != "Archived" && w.Status != "Deleted");

            Workflows = await fallbackQuery
                .OrderByDescending(w => w.UpdatedAtUtc)
                .Select(w => new WorkflowListItem
                {
                    Id = w.Id,
                    DisplayId = w.DisplayId,
                    Dguid = w.Dguid,
                    Name = w.Name,
                    Caption = w.Caption ?? w.Name,
                    WorkflowType = w.WorkflowType,
                    ApplicationId = w.ApplicationId,
                    FileListId = w.FileListId,
                    ViewOnApplication = w.ViewOnApplication,
                    StartupType = w.StartupType,
                    StartupArgument1 = w.StartupArgument1,
                    StartupArgument2 = w.StartupArgument2,
                    InActive = w.InActive,
                    Diagram = w.Diagram,
                    KpiActivity = w.KpiActivity,
                    CreatedBy = w.CreatedByName,
                    Status = w.Status,
                    TriggerType = w.TriggerType,
                    CreatedAtUtc = w.CreatedAtUtc,
                    UpdatedAtUtc = w.UpdatedAtUtc
                })
                .ToListAsync();
        }

        return Page();
    }

    public async Task<IActionResult> OnGetListAsync()
    {
        await EnsureWorkflowSchemaAsync();
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(Array.Empty<object>());
        }

        var workflows = await _db.Workflows
            .Where(w => w.OwnerUserId == user.Id
                        && (user.CompanyId.HasValue ? w.CompanyId == user.CompanyId : w.CompanyId == null)
                        && w.Status != "Archived"
                        && w.Status != "Deleted")
            .OrderBy(w => w.Name)
            .Select(w => new
            {
                id = w.Id,
                name = w.Caption ?? w.Name,
                status = w.Status,
                dguid = w.Dguid,
                inActive = w.InActive,
                updatedAtUtc = w.UpdatedAtUtc
            })
            .ToListAsync();

        if (workflows.Count == 0)
        {
            workflows = await _db.Workflows
                .Where(w => w.Status != "Archived" && w.Status != "Deleted")
                .OrderBy(w => w.Name)
                .Select(w => new
                {
                    id = w.Id,
                    name = w.Caption ?? w.Name,
                    status = w.Status,
                    dguid = w.Dguid,
                    inActive = w.InActive,
                    updatedAtUtc = w.UpdatedAtUtc
                })
                .ToListAsync();
        }

        return new JsonResult(workflows);
    }

    public async Task<IActionResult> OnGetArchivedAsync()
    {
        await EnsureWorkflowSchemaAsync();
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in." }) { StatusCode = 401 };
        }

        IQueryable<Workflow> query = _db.Workflows
            .Where(w => w.OwnerUserId == user.Id);
        query = user.CompanyId.HasValue
            ? query.Where(w => w.CompanyId == user.CompanyId)
            : query.Where(w => w.CompanyId == null);

        var archived = await query
            .Where(w => w.Status == "Archived")
            .OrderByDescending(w => w.UpdatedAtUtc)
            .Select(w => new
            {
                id = w.Id,
                displayId = w.DisplayId,
                name = w.Caption ?? w.Name,
                status = w.Status,
                triggerType = w.TriggerType,
                updatedAtUtc = w.UpdatedAtUtc
            })
            .ToListAsync();

        return new JsonResult(new { success = true, archived });
    }

    public async Task<IActionResult> OnPostCreateAsync([FromBody] CreateWorkflowPayload? payload)
    {
        await EnsureWorkflowSchemaAsync();
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to create workflows." }) { StatusCode = 401 };
        }

        if (payload == null)
        {
            return new JsonResult(new { success = false, message = "Invalid workflow request payload." }) { StatusCode = 400 };
        }

        var name = payload.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            return new JsonResult(new { success = false, message = "Please provide a workflow name." }) { StatusCode = 400 };
        }

        if (name.Length > 120)
        {
            return new JsonResult(new { success = false, message = "Workflow name must be 120 characters or less." }) { StatusCode = 400 };
        }

        var description = string.IsNullOrWhiteSpace(payload.Description) ? null : payload.Description.Trim();
        if (description is { Length: > 320 })
        {
            return new JsonResult(new { success = false, message = "Description must be 320 characters or less." }) { StatusCode = 400 };
        }

        var triggerType = string.IsNullOrWhiteSpace(payload.TriggerType) ? "Manual" : payload.TriggerType.Trim();
        if (triggerType.Length > 40)
        {
            triggerType = triggerType[..40];
        }

        var displayName = user.GetFriendlyName();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = user.Email ?? "Bugence User";
        }
        if (displayName.Length > 180)
        {
            displayName = displayName[..180];
        }

        var baseDisplayId = await GetNextDisplayIdAsync(user.Id);
        Workflow? workflow = null;
        const int maxCreateAttempts = 30;
        for (var attempt = 1; attempt <= maxCreateAttempts; attempt++)
        {
            workflow = BuildWorkflow(
                userId: user.Id,
                companyId: user.CompanyId,
                displayId: baseDisplayId + (attempt - 1),
                createdByName: displayName,
                name: name,
                description: description,
                triggerType: triggerType);

            try
            {
                _db.Workflows.Add(workflow);
                await _db.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException ex) when (IsWorkflowDisplayIdConflict(ex))
            {
                _db.Entry(workflow).State = EntityState.Detached;
                if (attempt == maxCreateAttempts)
                {
                    return new JsonResult(new
                    {
                        success = false,
                        message = "Unable to create workflow right now. Please try again."
                    })
                    { StatusCode = 409 };
                }
            }
            catch (DbUpdateException ex)
            {
                return new JsonResult(new
                {
                    success = false,
                    message = $"Unable to create workflow: {ex.GetBaseException().Message}"
                })
                { StatusCode = 500 };
            }
            catch (Exception ex)
            {
                return new JsonResult(new
                {
                    success = false,
                    message = $"Unable to create workflow: {ex.GetBaseException().Message}"
                })
                { StatusCode = 500 };
            }
        }

        if (workflow == null)
        {
            return new JsonResult(new
            {
                success = false,
                message = "Unable to create workflow right now. Please try again."
            })
            { StatusCode = 500 };
        }

        return new JsonResult(new
        {
            success = true,
            redirectUrl = $"/Workflows/Editor?id={workflow.Id}",
            workflow = new
            {
                id = workflow.Id,
                displayId = workflow.DisplayId,
                dguid = workflow.Dguid,
                name = workflow.Name,
                caption = workflow.Caption,
                workflowType = workflow.WorkflowType,
                applicationId = workflow.ApplicationId,
                fileListId = workflow.FileListId,
                viewOnApplication = workflow.ViewOnApplication,
                startupType = workflow.StartupType,
                startupArgument1 = workflow.StartupArgument1,
                startupArgument2 = workflow.StartupArgument2,
                inActive = workflow.InActive,
                diagram = workflow.Diagram,
                kpiActivity = workflow.KpiActivity,
                updatedAtUtc = workflow.UpdatedAtUtc
            }
        });
    }

    public async Task<IActionResult> OnGetNextIdentityAsync()
    {
        await EnsureWorkflowSchemaAsync();
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in." }) { StatusCode = 401 };
        }

        var nextId = await GetNextDisplayIdAsync(user.Id);
        return new JsonResult(new
        {
            success = true,
            nextId,
            dguid = Guid.NewGuid().ToString("N")
        });
    }

    public async Task<IActionResult> OnPostRenameAsync([FromBody] RenameWorkflowPayload payload)
    {
        await EnsureWorkflowSchemaAsync();
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to rename workflows." }) { StatusCode = 401 };
        }

        if (!ModelState.IsValid)
        {
            return new JsonResult(new { success = false, message = "Please provide a valid workflow name." }) { StatusCode = 400 };
        }

        var workflow = await _db.Workflows.FirstOrDefaultAsync(w => w.Id == payload.Id
            && w.OwnerUserId == user.Id
            && (user.CompanyId.HasValue ? w.CompanyId == user.CompanyId : w.CompanyId == null));
        if (workflow == null)
        {
            return new JsonResult(new { success = false, message = "Workflow not found." }) { StatusCode = 404 };
        }

        workflow.Name = payload.Name.Trim();
        workflow.Caption = payload.Name.Trim();
        workflow.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new JsonResult(new { success = true, name = workflow.Name });
    }

    public async Task<IActionResult> OnPostArchiveAsync([FromBody] WorkflowActionPayload payload)
    {
        try
        {
            await EnsureWorkflowSchemaAsync();
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return new JsonResult(new { success = false, message = "Please sign in to archive workflows." }) { StatusCode = 401 };
            }

            if (!ModelState.IsValid)
            {
                return new JsonResult(new { success = false, message = "Invalid workflow request." }) { StatusCode = 400 };
            }

            var workflow = await _db.Workflows.FirstOrDefaultAsync(w => w.Id == payload.Id
                && w.OwnerUserId == user.Id
                && (user.CompanyId.HasValue ? w.CompanyId == user.CompanyId : w.CompanyId == null));
            if (workflow == null)
            {
                return new JsonResult(new { success = false, message = "Workflow not found." }) { StatusCode = 404 };
            }

            workflow.Status = "Archived";
            workflow.UpdatedAtUtc = DateTime.UtcNow;
            await DisconnectWorkflowBindingsAsync(workflow);
            await _db.SaveChangesAsync();

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = $"Archive failed: {ex.GetBaseException().Message}" }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostUnarchiveAsync([FromBody] WorkflowActionPayload payload)
    {
        await EnsureWorkflowSchemaAsync();
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to unarchive workflows." }) { StatusCode = 401 };
        }

        if (!ModelState.IsValid)
        {
            return new JsonResult(new { success = false, message = "Invalid workflow request." }) { StatusCode = 400 };
        }

        var workflow = await _db.Workflows.FirstOrDefaultAsync(w => w.Id == payload.Id
            && w.OwnerUserId == user.Id
            && (user.CompanyId.HasValue ? w.CompanyId == user.CompanyId : w.CompanyId == null));
        if (workflow == null)
        {
            return new JsonResult(new { success = false, message = "Workflow not found." }) { StatusCode = 404 };
        }

        workflow.Status = "Draft";
        workflow.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new JsonResult(new { success = true });
    }

    public async Task<IActionResult> OnPostDeleteAsync([FromBody] WorkflowActionPayload payload)
    {
        try
        {
            await EnsureWorkflowSchemaAsync();
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return new JsonResult(new { success = false, message = "Please sign in to delete workflows." }) { StatusCode = 401 };
            }

            if (!ModelState.IsValid)
            {
                return new JsonResult(new { success = false, message = "Invalid workflow request." }) { StatusCode = 400 };
            }

            var workflow = await _db.Workflows.FirstOrDefaultAsync(w => w.Id == payload.Id
                && w.OwnerUserId == user.Id
                && (user.CompanyId.HasValue ? w.CompanyId == user.CompanyId : w.CompanyId == null));
            if (workflow == null)
            {
                return new JsonResult(new { success = false, message = "Workflow not found." }) { StatusCode = 404 };
            }

            workflow.Status = "Deleted";
            workflow.UpdatedAtUtc = DateTime.UtcNow;
            await DisconnectWorkflowBindingsAsync(workflow);
            await _db.SaveChangesAsync();

            return new JsonResult(new { success = true });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = $"Delete failed: {ex.GetBaseException().Message}" }) { StatusCode = 500 };
        }
    }

    public async Task<IActionResult> OnPostDatabaseSyncAsync()
    {
        await EnsureWorkflowSchemaAsync();
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return new JsonResult(new { success = false, message = "Please sign in to continue." }) { StatusCode = 401 };
        }

        var repaired = await _db.Workflows
            .Where(w => w.OwnerUserId == user.Id && w.CompanyId == null && user.CompanyId != null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.CompanyId, user.CompanyId)
                .SetProperty(w => w.UpdatedAtUtc, DateTime.UtcNow));

        return new JsonResult(new { success = true, repaired, message = $"Database synced. {repaired} workflow record(s) repaired." });
    }

    public class CreateWorkflowPayload
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string TriggerType { get; set; } = "Manual";
    }

    public class RenameWorkflowPayload
    {
        [Required]
        public Guid Id { get; set; }

        [Required, MinLength(2), MaxLength(120)]
        public string Name { get; set; } = string.Empty;
    }

    public class WorkflowActionPayload
    {
        [Required]
        public Guid Id { get; set; }
    }

    public class WorkflowListItem
    {
        public Guid Id { get; set; }
        public int DisplayId { get; set; }
        public string Dguid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public string WorkflowType { get; set; } = "Application Workflow";
        public string? ApplicationId { get; set; }
        public int FileListId { get; set; }
        public int ViewOnApplication { get; set; }
        public int StartupType { get; set; }
        public string? StartupArgument1 { get; set; }
        public string? StartupArgument2 { get; set; }
        public bool InActive { get; set; }
        public string? Diagram { get; set; }
        public string? KpiActivity { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public string Status { get; set; } = "Draft";
        public string TriggerType { get; set; } = "Manual";
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }

    private Task EnsureWorkflowSchemaAsync()
        => WorkflowSchemaService.EnsureLegacyColumnsAsync(_db);

    private async Task<int> GetNextDisplayIdAsync(string ownerUserId)
    {
        var maxId = await _db.Workflows
            .Select(w => (int?)w.DisplayId)
            .MaxAsync();

        return (maxId ?? 0) + 1;
    }

    private static Workflow BuildWorkflow(
        string userId,
        Guid? companyId,
        int displayId,
        string createdByName,
        string name,
        string? description,
        string triggerType)
    {
        return new Workflow
        {
            OwnerUserId = userId,
            CompanyId = companyId,
            DisplayId = displayId,
            Dguid = Guid.NewGuid().ToString("N"),
            CreatedByName = createdByName,
            Name = name,
            Caption = name,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Status = "Draft",
            TriggerType = triggerType,
            WorkflowType = "Application Workflow",
            ApplicationId = "Activities",
            FileListId = 0,
            ViewOnApplication = 0,
            StartupType = 1,
            StartupArgument1 = string.Empty,
            StartupArgument2 = string.Empty,
            InActive = false,
            KpiActivity = string.Empty,
            DefinitionJson = "{\"nodes\":[],\"edges\":[],\"meta\":{\"version\":1}}",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static bool IsWorkflowDisplayIdConflict(DbUpdateException ex)
    {
        if (ex.InnerException is SqliteException sqliteEx && sqliteEx.SqliteErrorCode == 19)
        {
            return sqliteEx.Message.Contains("Workflows.OwnerUserId, Workflows.DisplayId", StringComparison.OrdinalIgnoreCase)
                || sqliteEx.Message.Contains("IX_Workflows_OwnerUserId_DisplayId", StringComparison.OrdinalIgnoreCase);
        }

        var message = ex.GetBaseException().Message;
        return message.Contains("Workflows.OwnerUserId, Workflows.DisplayId", StringComparison.OrdinalIgnoreCase)
            || message.Contains("IX_Workflows_OwnerUserId_DisplayId", StringComparison.OrdinalIgnoreCase);
    }

    private async Task DisconnectWorkflowBindingsAsync(Workflow workflow)
    {
        var normalizedDguid = NormalizeDguid(workflow.Dguid);
        var bindingsById = await _db.DynamicVeActionBindings
            .Where(b => b.WorkflowId.HasValue && b.WorkflowId == workflow.Id)
            .ToListAsync();

        var bindingsByDguid = new List<DynamicVeActionBinding>();
        if (!string.IsNullOrWhiteSpace(normalizedDguid))
        {
            var dguidCandidates = await _db.DynamicVeActionBindings
                .Where(b => b.WorkflowDguid != null && b.WorkflowDguid != string.Empty)
                .ToListAsync();
            bindingsByDguid = dguidCandidates
                .Where(b => NormalizeDguid(b.WorkflowDguid) == normalizedDguid)
                .ToList();
        }

        var bindings = bindingsById
            .Concat(bindingsByDguid)
            .GroupBy(b => b.Id)
            .Select(g => g.First())
            .ToList();

        foreach (var binding in bindings)
        {
            binding.WorkflowId = null;
            binding.WorkflowDguid = null;
            binding.WorkflowNameSnapshot = null;

            // Once archived/deleted, this binding should not trigger workflow execution.
            if (string.Equals(binding.ActionType, "workflow", StringComparison.OrdinalIgnoreCase))
            {
                binding.ActionType = "navigate";
            }
            else if (string.Equals(binding.ActionType, "hybrid", StringComparison.OrdinalIgnoreCase))
            {
                binding.ActionType = "navigate";
            }
        }
    }

    private static string NormalizeDguid(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("-", string.Empty, StringComparison.Ordinal).Trim().ToLowerInvariant();
}
