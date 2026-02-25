using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace BugenceEditConsole.Services;

public interface ISubscriptionService
{
    Task<UserSubscription> GetOrStartTrialAsync(string userId);
    Task<UserSubscription?> GetActiveAsync(string userId);
    Task<bool> IsAccessLockedAsync(string userId);
    Task MarkSubscriptionAsync(string userId, string planKey, SubscriptionInterval interval, string status, string? provider = null);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SubscriptionService> _logger;
    private readonly int _trialHours;

    public SubscriptionService(ApplicationDbContext db, IConfiguration config, ILogger<SubscriptionService> logger)
    {
        _db = db;
        _logger = logger;
        _trialHours = Math.Max(1, config.GetValue<int?>("Billing:TrialHours") ?? 1);
    }

    public async Task<UserSubscription> GetOrStartTrialAsync(string userId)
    {
        var existing = await _db.UserSubscriptions
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(s => s.UserId == userId);

        if (existing != null)
        {
            return existing;
        }

        var trial = new UserSubscription
        {
            UserId = userId,
            PlanKey = "Starter",
            Status = "Trial",
            Interval = SubscriptionInterval.Trial,
            CurrentPeriodStartUtc = DateTime.UtcNow,
            CurrentPeriodEndUtc = DateTime.UtcNow.AddHours(_trialHours),
            TrialEndsUtc = DateTime.UtcNow.AddHours(_trialHours),
            Provider = "Trial"
        };
        _db.UserSubscriptions.Add(trial);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Started trial for user {UserId}", userId);
        return trial;
    }

    public async Task<UserSubscription?> GetActiveAsync(string userId)
    {
        return await _db.UserSubscriptions
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(s => s.UserId == userId);
    }

    public async Task<bool> IsAccessLockedAsync(string userId)
    {
        var sub = await GetActiveAsync(userId);
        if (sub == null)
        {
            return false;
        }

        if (string.Equals(sub.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(sub.Status, "Trial", StringComparison.OrdinalIgnoreCase))
        {
            return sub.TrialEndsUtc.HasValue && sub.TrialEndsUtc.Value <= DateTime.UtcNow;
        }

        return true;
    }

    public async Task MarkSubscriptionAsync(string userId, string planKey, SubscriptionInterval interval, string status, string? provider = null)
    {
        var sub = await _db.UserSubscriptions
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(s => s.UserId == userId);

        if (sub == null)
        {
            sub = new UserSubscription { UserId = userId };
            _db.UserSubscriptions.Add(sub);
        }

        sub.PlanKey = planKey;
        sub.Interval = interval;
        sub.Status = status;
        sub.Provider = provider ?? sub.Provider;
        sub.UpdatedAtUtc = DateTime.UtcNow;
        if (interval != SubscriptionInterval.Trial)
        {
            sub.CurrentPeriodStartUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }
}
