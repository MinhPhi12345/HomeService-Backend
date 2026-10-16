using HomeService.Api.Data;
using HomeService.Api.Enums;
using HomeService.Api.Models;
using HomeService.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HomeService.Api.Services;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db)
    {
        _db = db;
    }

    public void Add(int userId, string title, string message)
    {
        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            Title = Truncate(title, 150),
            Message = Truncate(message, 500),
        });
    }

    public async Task AddForAdminsAsync(string title, string message)
    {
        var adminIds = await _db.Users
            .Where(u => u.Role == UserRole.Admin && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync();
        foreach (var id in adminIds) Add(id, title, message);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
