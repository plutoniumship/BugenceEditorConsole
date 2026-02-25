using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BugenceEditConsole.Pages;

public class SupportModel : PageModel
{
    public IReadOnlyList<SupportMetric> Metrics { get; private set; } = [];
    public IReadOnlyList<SupportChannel> Channels { get; private set; } = [];
    public IReadOnlyList<RapidPlaybook> Playbooks { get; private set; } = [];
    public IReadOnlyList<KnowledgeArticle> KnowledgeBase { get; private set; } = [];
    public IReadOnlyList<TicketDigest> TicketDigests { get; private set; } = [];
    public ServiceHeartbeat Heartbeat { get; private set; } = default!;

    public void OnGet()
    {
        Metrics = new[]
        {
            new SupportMetric("fa-solid fa-clock", "Live response window", "07:00 – 22:00 PT concierge coverage"),
            new SupportMetric("fa-solid fa-arrows-rotate", "Rollback SLA", "Average rollback in 4m 38s"),
            new SupportMetric("fa-solid fa-user-shield", "Client satisfaction", "97.4% 30-day CSAT"),
            new SupportMetric("fa-solid fa-diagram-project", "Active engagements", "12 concurrent operator missions")
        };

        Channels = new[]
        {
            new SupportChannel("fa-solid fa-envelope-open-text", "Email", "info@bugence.com"),
            new SupportChannel("fa-solid fa-comments", "Slack Connect", "#bugence-mission-support"),
            new SupportChannel("fa-solid fa-phone-volume", "Direct line", "+1 (415) 555-2048"),
            new SupportChannel("fa-solid fa-calendar-check", "Schedule concierge", "Book a 30-minute pairing session")
        };

        Playbooks = new[]
        {
            new RapidPlaybook("fa-solid fa-bolt", "Production rollback", "Tag ticket with ROLLBACK for a five minute restore.", "ROLLBACK"),
            new RapidPlaybook("fa-solid fa-flask", "Sandbox walkthrough", "Pair with engineering to validate new modules pre-launch.", null),
            new RapidPlaybook("fa-solid fa-graduation-cap", "Crew onboarding", "Tailored training with recorded macros, cheat sheets, and QA-ready scripts.", null),
            new RapidPlaybook("fa-solid fa-chart-line", "Stakeholder reporting", "Request weekly mission summaries with metrics and highlights.", null)
        };

        KnowledgeBase = new[]
        {
            new KnowledgeArticle("Launch checklist for Bugence", "Step-by-step runbook to deploy new hero pages safely.", "/docs/launch-checklist"),
            new KnowledgeArticle("Automation guardrails explained", "How Bugence enforces zero-trust protections across your missions.", "/docs/automation-guardrails"),
            new KnowledgeArticle("Concierge escalation matrix", "Know when to escalate to engineering, design, or narrative squads.", "/docs/escalation-matrix")
        };

        TicketDigests = new[]
        {
            new TicketDigest("OPS-2219", "Hero imagery sync offset", DateTime.UtcNow.AddHours(-3), "In progress"),
            new TicketDigest("OPS-2213", "Community CTA copy refresh", DateTime.UtcNow.AddDays(-1), "Resolved"),
            new TicketDigest("OPS-2208", "Analytics feed handshake", DateTime.UtcNow.AddDays(-3), "Monitoring")
        };

        Heartbeat = new ServiceHeartbeat(
            UptimePercentage: 99.978m,
            IncidentsThisQuarter: 1,
            LastIncidentUtc: DateTime.UtcNow.AddDays(-11),
            NextMaintenanceUtc: DateTime.UtcNow.Date.AddDays(6).AddHours(2));
    }

    public record SupportMetric(string Icon, string Title, string Detail);
    public record SupportChannel(string Icon, string Title, string Description);
    public record RapidPlaybook(string Icon, string Title, string Detail, string? Tag);
    public record KnowledgeArticle(string Title, string Summary, string Link);
    public record TicketDigest(string Id, string Summary, DateTime SubmittedAtUtc, string Status);
    public record ServiceHeartbeat(decimal UptimePercentage, int IncidentsThisQuarter, DateTime LastIncidentUtc, DateTime NextMaintenanceUtc);
}
