using System.Text.Json;
using System.Text.RegularExpressions;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public record WorkflowTriggerContext(
    string? Email,
    IDictionary<string, string?> Fields,
    string? SourceUrl,
    string? ElementTag,
    string? ElementId,
    string? Provider = null,
    string? BranchKey = null,
    string? RawPayloadJson = null,
    IDictionary<string, string?>? MappedFields = null,
    IDictionary<string, bool>? ValidationFlags = null);

public class WorkflowExecutionService
{
    private static readonly Regex CurlyTokenRegex = new(@"\{\{\s*([A-Za-z0-9_\-.:]+)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex CaretTokenRegex = new(@"\^\s*([A-Za-z0-9_\-.:]+)\s*\^", RegexOptions.Compiled);

    private readonly IEmailSender _emailSender;
    private readonly ApplicationDbContext _db;
    private readonly ISensitiveDataProtector _protector;
    private readonly ILogger<WorkflowExecutionService> _logger;

    public WorkflowExecutionService(IEmailSender emailSender, ApplicationDbContext db, ISensitiveDataProtector protector, ILogger<WorkflowExecutionService> logger)
    {
        _emailSender = emailSender;
        _db = db;
        _protector = protector;
        _logger = logger;
    }

    public async Task<(bool Success, string? Error)> ExecuteAsync(Workflow workflow, WorkflowTriggerContext context)
    {
        if (string.Equals(workflow.Status, "Paused", StringComparison.OrdinalIgnoreCase))
        {
            await LogAsync(workflow, "Failed", "Workflow is paused.", context.SourceUrl);
            return (false, "Workflow is paused.");
        }

        var emailNode = TryGetEmailNode(workflow.DefinitionJson);
        if (emailNode == null)
        {
            await LogAsync(workflow, "Failed", "No email step found.", context.SourceUrl);
            return (false, "No email step found.");
        }

        var recipient = await ResolveRecipientAsync(workflow, context, emailNode.MailTo, emailNode.EmailAddress, emailNode.EmailAddressField);
        if (string.IsNullOrWhiteSpace(recipient))
        {
            await LogAsync(workflow, "Failed", "Recipient email is missing.", context.SourceUrl);
            return (false, "Recipient email is missing.");
        }

        var tokens = BuildTokenMap(context);
        var unresolvedTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var subjectResult = ApplyTokens(emailNode.Subject ?? "Bugence Workflow", tokens, htmlEncodeValues: false);
        var bodyResult = ApplyTokens(emailNode.Body ?? "<p>Hello!</p>", tokens, htmlEncodeValues: true);
        unresolvedTokens.UnionWith(subjectResult.UnresolvedTokens);
        unresolvedTokens.UnionWith(bodyResult.UnresolvedTokens);

        if (unresolvedTokens.Count > 0)
        {
            _logger.LogWarning(
                "Workflow {WorkflowId} email template has unresolved tokens: {Tokens}. Source: {SourceUrl}",
                workflow.Id,
                string.Join(", ", unresolvedTokens.OrderBy(t => t)),
                context.SourceUrl ?? "-");
        }

        var (sent, error, transportPath) = await SendEmailAsync(workflow, emailNode, recipient, subjectResult.Value, bodyResult.Value);
        _logger.LogInformation(
            "Workflow {WorkflowId} email transport path: {TransportPath}",
            workflow.Id,
            transportPath);
        if (!sent)
        {
            _logger.LogWarning("Workflow email failed for {WorkflowId}: {Error}", workflow.Id, error);
            await LogAsync(workflow, "Failed", error ?? "Unable to deliver email.", context.SourceUrl, "Email");
            return (false, error ?? "Unable to deliver email.");
        }

        await LogAsync(workflow, "Success", "Email delivered.", context.SourceUrl, "Email");
        return (true, null);
    }

    private async Task LogAsync(Workflow workflow, string status, string message, string? sourceUrl, string? stepName = null)
    {
        try
        {
            _db.WorkflowExecutionLogs.Add(new WorkflowExecutionLog
            {
                WorkflowId = workflow.Id,
                OwnerUserId = workflow.OwnerUserId,
                Status = status,
                Message = message,
                StepName = stepName,
                SourceUrl = sourceUrl,
                ExecutedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write workflow execution log.");
        }
    }

    private async Task<(bool Success, string? Error, string TransportPath)> SendEmailAsync(
        Workflow workflow,
        WorkflowEmailNode emailNode,
        string recipient,
        string subject,
        string body)
    {
        if (!string.IsNullOrWhiteSpace(emailNode.SmtpProfileDguid))
        {
            var profile = await SystemPropertySmtpLoader.FindProfileAsync(
                _db,
                _protector,
                workflow.OwnerUserId,
                workflow.CompanyId,
                emailNode.SmtpProfileDguid);
            if (profile != null)
            {
                var (sent, error) = await SendWithProfileAsync(profile, recipient, subject, body);
                return (sent, error, "selected-profile");
            }
        }

        var defaultProfile = await SystemPropertySmtpLoader.FindDefaultProfileAsync(
            _db,
            _protector,
            workflow.OwnerUserId,
            workflow.CompanyId);
        if (defaultProfile != null)
        {
            var (sent, error) = await SendWithProfileAsync(defaultProfile, recipient, subject, body);
            return (sent, error, "default-system-profile");
        }

        var fallbackResult = await _emailSender.SendAsync(recipient, subject, body);
        return (fallbackResult.Success, fallbackResult.Error, "app-fallback");
    }

    private async Task<(bool Success, string? Error)> SendWithProfileAsync(
        SystemPropertySmtpLoader.SmtpProfile profile,
        string recipient,
        string subject,
        string body)
    {
        if (string.IsNullOrWhiteSpace(profile.Host) || string.IsNullOrWhiteSpace(profile.FromAddress))
        {
            return (false, "Selected SMTP profile is incomplete (host/from address missing).");
        }
        if (!string.IsNullOrWhiteSpace(profile.Username) && string.IsNullOrWhiteSpace(profile.Password))
        {
            return (false, "Selected SMTP profile password is missing or could not be decrypted. Re-save SMTP password in System Properties.");
        }

        try
        {
            using var client = new SmtpClient(profile.Host, profile.Port)
            {
                EnableSsl = profile.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Timeout = 10000
            };

            if (!string.IsNullOrWhiteSpace(profile.Username))
            {
                client.Credentials = new NetworkCredential(profile.Username, profile.Password);
            }

            var message = new MailMessage
            {
                From = new MailAddress(profile.FromAddress, string.IsNullOrWhiteSpace(profile.FromName) ? "Bugence" : profile.FromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            message.To.Add(recipient);

            await client.SendMailAsync(message);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow SMTP profile send failed.");
            return (false, $"SMTP profile send failed: {ex.Message}");
        }
    }

    private async Task<string?> ResolveRecipientAsync(
        Workflow workflow,
        WorkflowTriggerContext context,
        string? mailTo,
        string? emailAddress,
        string? emailAddressField)
    {
        if (!string.IsNullOrWhiteSpace(emailAddress) && emailAddress.Contains('@', StringComparison.Ordinal))
        {
            return emailAddress.Trim();
        }

        if (!string.IsNullOrWhiteSpace(mailTo) &&
            mailTo.Equals("Current User", StringComparison.OrdinalIgnoreCase))
        {
            var ownerEmail = await _db.Users
                .Where(u => u.Id == workflow.OwnerUserId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(ownerEmail))
            {
                return ownerEmail.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(mailTo) &&
            !mailTo.Equals("Current User", StringComparison.OrdinalIgnoreCase) &&
            mailTo.Contains('@', StringComparison.Ordinal))
        {
            return mailTo.Trim();
        }

        if (!string.IsNullOrWhiteSpace(emailAddressField) &&
            context.Fields.TryGetValue(emailAddressField, out var mappedEmail) &&
            !string.IsNullOrWhiteSpace(mappedEmail))
        {
            return mappedEmail;
        }

        if (!string.IsNullOrWhiteSpace(context.Email))
        {
            return context.Email;
        }

        if (context.Fields.TryGetValue("email", out var email) && !string.IsNullOrWhiteSpace(email))
        {
            return email;
        }

        var fallback = context.Fields
            .FirstOrDefault(kvp => kvp.Key.Contains("email", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kvp.Value));
        return string.IsNullOrWhiteSpace(fallback.Value) ? null : fallback.Value;
    }

    private static Dictionary<string, string?> BuildTokenMap(WorkflowTriggerContext context)
    {
        var tokens = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["email"] = context.Email,
            ["sourceUrl"] = context.SourceUrl,
            ["elementTag"] = context.ElementTag,
            ["elementId"] = context.ElementId,
            ["provider"] = context.Provider,
            ["branchKey"] = context.BranchKey
        };
        foreach (var field in context.Fields)
        {
            tokens[field.Key] = field.Value;
            AddNormalizedTokenAliases(tokens, field.Key, field.Value);
        }
        if (context.MappedFields != null)
        {
            foreach (var field in context.MappedFields)
            {
                tokens[field.Key] = field.Value;
                AddNormalizedTokenAliases(tokens, field.Key, field.Value);
            }
        }
        if (context.ValidationFlags != null)
        {
            foreach (var flag in context.ValidationFlags)
            {
                tokens[flag.Key] = flag.Value ? "true" : "false";
            }
        }

        // Canonical aliases for contact workflows
        var details = FirstTokenValue(tokens, "details", "message", "inquiry", "comment", "description", "body", "text");
        if (!string.IsNullOrWhiteSpace(details))
        {
            tokens["details"] = details;
            if (!tokens.ContainsKey("message") || string.IsNullOrWhiteSpace(tokens["message"]))
            {
                tokens["message"] = details;
            }
        }

        var fullName = FirstTokenValue(tokens, "fullName", "fullname", "full_name", "name");
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            tokens["fullName"] = fullName;
            if (!tokens.ContainsKey("name") || string.IsNullOrWhiteSpace(tokens["name"]))
            {
                tokens["name"] = fullName;
            }
        }

        return tokens;
    }

    private static string? FirstTokenValue(IReadOnlyDictionary<string, string?> tokens, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (tokens.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static void AddNormalizedTokenAliases(IDictionary<string, string?> tokens, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var trimmed = key.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        var lower = trimmed.ToLowerInvariant();
        tokens[lower] = value;

        var compact = new string(lower.Where(char.IsLetterOrDigit).ToArray());
        if (!string.IsNullOrWhiteSpace(compact))
        {
            tokens[compact] = value;
        }
    }

    private static TokenApplyResult ApplyTokens(string template, IReadOnlyDictionary<string, string?> tokens, bool htmlEncodeValues)
    {
        var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string ReplaceToken(Match match)
        {
            var rawKey = match.Groups.Count > 1 ? match.Groups[1].Value : string.Empty;
            var key = rawKey?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                return match.Value;
            }

            if (!tokens.TryGetValue(key, out var value) || value == null)
            {
                unresolved.Add(key);
                return match.Value;
            }

            return htmlEncodeValues ? WebUtility.HtmlEncode(value) : value;
        }

        var result = CurlyTokenRegex.Replace(template, new MatchEvaluator(ReplaceToken));
        result = CaretTokenRegex.Replace(result, new MatchEvaluator(ReplaceToken));

        return new TokenApplyResult(result, unresolved);
    }

    private sealed class TokenApplyResult
    {
        public TokenApplyResult(string value, IReadOnlyCollection<string> unresolvedTokens)
        {
            Value = value;
            UnresolvedTokens = unresolvedTokens;
        }

        public string Value { get; }
        public IReadOnlyCollection<string> UnresolvedTokens { get; }
    }

    private static WorkflowEmailNode? TryGetEmailNode(string definitionJson)
    {
        if (string.IsNullOrWhiteSpace(definitionJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(definitionJson);
            if (!doc.RootElement.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            foreach (var node in nodes.EnumerateArray())
            {
                if (!node.TryGetProperty("type", out var typeProp)) continue;
                if (!string.Equals(typeProp.GetString(), "email", StringComparison.OrdinalIgnoreCase)) continue;
                if (!node.TryGetProperty("data", out var data)) continue;
                JsonElement? actionProps = data.TryGetProperty("actionProps", out var actionPropsProp) && actionPropsProp.ValueKind == JsonValueKind.Object
                    ? actionPropsProp
                    : null;

                string? actionPropString(string key)
                    => actionProps.HasValue && actionProps.Value.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
                        ? prop.GetString()
                        : null;

                return new WorkflowEmailNode
                {
                    Subject = actionPropString("emailSubject")
                        ?? actionPropString("emailTitle")
                        ?? (data.TryGetProperty("subject", out var subjectProp) ? subjectProp.GetString() : null),
                    Body = actionPropString("emailBody")
                        ?? (data.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : null),
                    MailTo = actionPropString("mailTo"),
                    EmailAddress = actionPropString("emailAddress"),
                    EmailAddressField = actionPropString("emailAddressField"),
                    SmtpProfileDguid = actionPropString("smtpProfileDguid")
                };
            }
        }
        catch
        {
            return null;
        }
        return null;
    }

    private sealed class WorkflowEmailNode
    {
        public string? Subject { get; set; }
        public string? Body { get; set; }
        public string? MailTo { get; set; }
        public string? EmailAddress { get; set; }
        public string? EmailAddressField { get; set; }
        public string? SmtpProfileDguid { get; set; }
    }
}
