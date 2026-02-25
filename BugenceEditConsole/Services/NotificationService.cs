using BugenceEditConsole.Data;
using BugenceEditConsole.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BugenceEditConsole.Services;

public interface INotificationService
{
    Task AddAsync(string userId, string title, string message, string type, object? metadata = null);
    Task<List<UserNotification>> GetRecentAsync(string userId, int take, bool includeRead);
    Task MarkAllReadAsync(string userId);
}

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _db;

    public NotificationService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(string userId, string title, string message, string type, object? metadata = null)
    {
        var notification = new UserNotification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            MetadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata),
            IsRead = false,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.UserNotifications.Add(notification);
        await _db.SaveChangesAsync();
    }

    public async Task<List<UserNotification>> GetRecentAsync(string userId, int take, bool includeRead)
    {
        var query = _db.UserNotifications.AsNoTracking()
            .Where(n => n.UserId == userId);
        if (!includeRead)
        {
            query = query.Where(n => !n.IsRead);
        }
        return await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(take)
            .ToListAsync();
    }

    public async Task MarkAllReadAsync(string userId)
    {
        var items = await _db.UserNotifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();
        if (items.Count == 0) return;
        foreach (var item in items)
        {
            item.IsRead = true;
        }
        await _db.SaveChangesAsync();
    }
}
