using LibraryManagementSystem.Data;
using LibraryManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagementSystem.Services;

public sealed class AuditService(
    IDbContextFactory<LibraryDbContext> factory,
    SessionService session,
    AuthorizationService authorization)
{
    public void Add(
        LibraryDbContext db,
        string action,
        string entityName,
        object? entityId = null,
        string? details = null,
        int? userId = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = entityName,
            EntityId = entityId?.ToString(),
            Details = details,
            UserId = userId ?? session.Current?.UserId,
            CreatedAt = DateTime.UtcNow
        });
    }

    public async Task RecordAsync(
        string action,
        string entityName,
        object? entityId = null,
        string? details = null,
        int? userId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        Add(db, action, entityName, entityId, details, userId);
        await db.SaveChangesAsync();
    }

    public async Task<List<AuditLog>> GetRecentAsync(int count = 200)
    {
        authorization.Demand(Permission.ViewAuditLog);
        await using var db = await factory.CreateDbContextAsync();
        return await db.AuditLogs.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(count, 1, 1000))
            .ToListAsync();
    }
}
