namespace BugenceEditConsole.Services;

public record BillingPlan(
    string Key,
    string Name,
    string Description,
    int ProjectLimit,
    long StorageLimitBytes,
    int CollaboratorLimit,
    string[] Features,
    decimal MonthlyPrice,
    decimal SixMonthPrice,
    decimal YearlyPrice,
    bool IsCustom);

public static class BillingPlanCatalog
{
    public static readonly IReadOnlyList<BillingPlan> Plans = new[]
    {
        new BillingPlan(
            Key: "Starter",
            Name: "Starter",
            Description: "For hobbyists and personal exploration.",
            ProjectLimit: 1,
            StorageLimitBytes: 100L * 1024 * 1024,
            CollaboratorLimit: 0,
            Features: new[]
            {
                "1 Active Project",
                "100 MB Storage",
                "bugence.app subdomain",
                "Community support"
            },
            MonthlyPrice: 0,
            SixMonthPrice: 0,
            YearlyPrice: 0,
            IsCustom: false),
        new BillingPlan(
            Key: "Pro",
            Name: "Pro",
            Description: "For professional freelancers and power users.",
            ProjectLimit: 3,
            StorageLimitBytes: 5L * 1024 * 1024 * 1024,
            CollaboratorLimit: 0,
            Features: new[]
            {
                "Up to 3 Projects",
                "5 GB High-Speed Storage",
                "1 Custom Domain",
                "Advanced Text Editor",
                "Full Analytics",
                "Deployment History"
            },
            MonthlyPrice: 24,
            SixMonthPrice: 129,
            YearlyPrice: 230,
            IsCustom: false),
        new BillingPlan(
            Key: "Team",
            Name: "Team",
            Description: "For small agencies and startup teams.",
            ProjectLimit: 3,
            StorageLimitBytes: 50L * 1024 * 1024 * 1024,
            CollaboratorLimit: 3,
            Features: new[]
            {
                "3 Projects",
                "50 GB Shared Storage",
                "Up to 3 Collaborators",
                "Priority Support",
                "Shared Library"
            },
            MonthlyPrice: 59,
            SixMonthPrice: 318,
            YearlyPrice: 566,
            IsCustom: false),
        new BillingPlan(
            Key: "Enterprise",
            Name: "Enterprise",
            Description: "Custom plan for large organizations.",
            ProjectLimit: 0,
            StorageLimitBytes: 0,
            CollaboratorLimit: 0,
            Features: new[] { "Custom pricing", "Dedicated success team", "Security reviews" },
            MonthlyPrice: 0,
            SixMonthPrice: 0,
            YearlyPrice: 0,
            IsCustom: true)
    };

    public static BillingPlan GetPlan(string? key)
        => Plans.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
           ?? Plans[0];
}
