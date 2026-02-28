using System.Security.Cryptography;
using System.Text;
using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public sealed class LeadDedupeService
{
    private readonly ApplicationDbContext _db;

    public LeadDedupeService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<bool> IsDuplicateAsync(
        Guid workflowId,
        string actionNodeId,
        string? leadId,
        IDictionary<string, string?> mappedFields,
        int replayWindowMinutes,
        CancellationToken cancellationToken)
    {
        var keys = new List<(string Type, string Value)>();
        var email = mappedFields.TryGetValue("email", out var e) ? e : null;
        var phone = mappedFields.TryGetValue("phone", out var p) ? p : null;
        if (!string.IsNullOrWhiteSpace(email)) keys.Add(("email", email.Trim().ToLowerInvariant()));
        if (!string.IsNullOrWhiteSpace(phone)) keys.Add(("phone", phone.Trim()));
        if (!string.IsNullOrWhiteSpace(leadId)) keys.Add(("facebook_lead_id", leadId.Trim()));

        if (keys.Count == 0)
        {
            return false;
        }

        var cutoff = DateTime.UtcNow.AddMinutes(-Math.Abs(replayWindowMinutes <= 0 ? 10 : replayWindowMinutes));
        foreach (var key in keys)
        {
            var hash = Hash(key.Value);
            var existing = await _db.WorkflowLeadDedupeStates
                .FirstOrDefaultAsync(x =>
                    x.WorkflowId == workflowId &&
                    x.ActionNodeId == actionNodeId &&
                    x.DedupeKeyType == key.Type &&
                    x.DedupeKeyValueHash == hash,
                    cancellationToken);
            if (existing != null)
            {
                var duplicate = existing.LastProcessedAtUtc >= cutoff;
                existing.LastProcessedAtUtc = DateTime.UtcNow;
                existing.LastLeadId = leadId ?? existing.LastLeadId;
                await _db.SaveChangesAsync(cancellationToken);
                if (duplicate)
                {
                    return true;
                }
            }
            else
            {
                _db.WorkflowLeadDedupeStates.Add(new WorkflowLeadDedupeState
                {
                    WorkflowId = workflowId,
                    ActionNodeId = actionNodeId,
                    DedupeKeyType = key.Type,
                    DedupeKeyValueHash = hash,
                    LastLeadId = leadId,
                    LastProcessedAtUtc = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return false;
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}
