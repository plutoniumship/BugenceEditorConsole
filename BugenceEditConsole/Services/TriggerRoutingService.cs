using BugenceEditConsole.Models;

namespace BugenceEditConsole.Services;

public sealed class TriggerRoutingService
{
    public TriggerRoutingDecisionDto Decide(
        bool isDuplicate,
        TriggerValidationConfigDto validation,
        IDictionary<string, bool> flags)
    {
        if (!flags.TryGetValue("has_contact", out var hasContact) || !hasContact)
        {
            return new TriggerRoutingDecisionDto { BranchKey = "needs_enrichment", Reason = "missing_contact", IsDuplicate = isDuplicate };
        }

        if (validation.RequireConsent && (!flags.TryGetValue("has_consent", out var hasConsent) || !hasConsent))
        {
            return new TriggerRoutingDecisionDto { BranchKey = "compliance_review", Reason = "missing_consent", IsDuplicate = isDuplicate };
        }

        return new TriggerRoutingDecisionDto
        {
            BranchKey = "primary",
            Reason = isDuplicate ? "duplicate_continue" : "ok",
            IsDuplicate = isDuplicate
        };
    }
}
