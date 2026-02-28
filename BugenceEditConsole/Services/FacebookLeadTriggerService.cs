using System.Text.Json;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public sealed class FacebookLeadTriggerService
{
    private readonly ApplicationDbContext _db;
    private readonly MetaGraphClient _metaGraphClient;
    private readonly LeadMappingService _leadMappingService;
    private readonly LeadDedupeService _leadDedupeService;
    private readonly TriggerRoutingService _triggerRoutingService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly WorkflowExecutionService _workflowExecutionService;
    private readonly ILogger<FacebookLeadTriggerService> _logger;

    public FacebookLeadTriggerService(
        ApplicationDbContext db,
        MetaGraphClient metaGraphClient,
        LeadMappingService leadMappingService,
        LeadDedupeService leadDedupeService,
        TriggerRoutingService triggerRoutingService,
        UserManager<ApplicationUser> userManager,
        WorkflowExecutionService workflowExecutionService,
        ILogger<FacebookLeadTriggerService> logger)
    {
        _db = db;
        _metaGraphClient = metaGraphClient;
        _leadMappingService = leadMappingService;
        _leadDedupeService = leadDedupeService;
        _triggerRoutingService = triggerRoutingService;
        _userManager = userManager;
        _workflowExecutionService = workflowExecutionService;
        _logger = logger;
    }

    public async Task<object> ExecuteTestReplayAsync(
        Workflow workflow,
        WorkflowTriggerConfig triggerConfig,
        FacebookLeadTriggerReplayRequest? request,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(workflow.OwnerUserId);
        if (user == null)
        {
            return new { success = false, message = "Workflow owner not found." };
        }

        var leadId = $"test_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        var leadPayload = await _metaGraphClient.GetLeadPayloadAsync(user, leadId, cancellationToken);
        if (request?.MockFields != null)
        {
            foreach (var kvp in request.MockFields)
            {
                leadPayload.FieldData[kvp.Key] = kvp.Value;
            }
        }

        var config = ToDto(triggerConfig);
        var (mappedFields, validationFlags) = _leadMappingService.Apply(leadPayload, config);
        var duplicate = await _leadDedupeService.IsDuplicateAsync(workflow.Id, triggerConfig.ActionNodeId, leadPayload.LeadId, mappedFields, triggerConfig.ReplayWindowMinutes, cancellationToken);
        var routing = _triggerRoutingService.Decide(duplicate, config.Validation, validationFlags);

        var log = new WorkflowTriggerEventLog
        {
            WorkflowId = workflow.Id,
            ActionNodeId = triggerConfig.ActionNodeId,
            Provider = "facebook",
            ExternalEventId = leadPayload.LeadId,
            LeadId = leadPayload.LeadId,
            Mode = "test",
            Outcome = "processed",
            Reason = routing.Reason,
            PayloadJson = JsonSerializer.Serialize(leadPayload),
            ProcessedAtUtc = DateTime.UtcNow
        };
        _db.WorkflowTriggerEventLogs.Add(log);
        await _db.SaveChangesAsync(cancellationToken);

        bool emitted = false;
        string? emitError = null;
        if (request?.EmitIntoWorkflow == true)
        {
            var context = new WorkflowTriggerContext(
                Email: mappedFields.TryGetValue("email", out var email) ? email : null,
                Fields: mappedFields,
                SourceUrl: null,
                ElementTag: "facebookLeadTrigger",
                ElementId: triggerConfig.ActionNodeId,
                Provider: "facebook",
                BranchKey: routing.BranchKey,
                RawPayloadJson: JsonSerializer.Serialize(leadPayload),
                MappedFields: mappedFields,
                ValidationFlags: validationFlags);

            var (ok, error) = await _workflowExecutionService.ExecuteAsync(workflow, context);
            emitted = ok;
            emitError = error;
        }

        return new
        {
            success = true,
            mode = "test",
            leadPayload,
            mappedFields,
            validationFlags,
            routing,
            emittedIntoWorkflow = emitted,
            emitError
        };
    }

    public async Task HandleLiveWebhookAsync(Guid workflowId, string actionNodeId, string leadId, CancellationToken cancellationToken)
    {
        var workflow = await _db.Workflows.FirstOrDefaultAsync(x => x.Id == workflowId, cancellationToken);
        if (workflow == null)
        {
            return;
        }

        var config = await _db.WorkflowTriggerConfigs.FirstOrDefaultAsync(x =>
            x.WorkflowId == workflowId &&
            x.ActionNodeId == actionNodeId &&
            x.TriggerType == "facebook_lead", cancellationToken);
        if (config == null)
        {
            return;
        }

        var replay = new FacebookLeadTriggerReplayRequest { EmitIntoWorkflow = true };
        await ExecuteTestReplayAsync(workflow, config, replay, cancellationToken);
    }

    private static FacebookLeadTriggerConfigDto ToDto(WorkflowTriggerConfig row)
    {
        IReadOnlyList<LeadMappingRuleDto> rules = [];
        try
        {
            rules = JsonSerializer.Deserialize<List<LeadMappingRuleDto>>(row.MappingJson ?? "[]") ?? [];
        }
        catch
        {
            // keep defaults
        }

        TriggerValidationConfigDto validation;
        try
        {
            validation = JsonSerializer.Deserialize<TriggerValidationConfigDto>(row.ValidationConfigJson ?? "{}") ?? new TriggerValidationConfigDto();
        }
        catch
        {
            validation = new TriggerValidationConfigDto();
        }

        return new FacebookLeadTriggerConfigDto
        {
            WorkflowId = row.WorkflowId,
            ActionNodeId = row.ActionNodeId,
            TriggerType = row.TriggerType,
            Mode = row.Mode,
            ConnectionId = row.ConnectionId,
            AdAccountId = row.AdAccountId,
            PageId = row.PageId,
            FormId = row.FormId,
            TriggerEvent = row.TriggerEvent,
            MappingMode = row.MappingMode,
            MappingRules = rules,
            Validation = validation,
            SplitFullName = true,
            NormalizePhone = true,
            ValidateEmail = true,
            DetectCountryFromPhone = true
        };
    }
}
