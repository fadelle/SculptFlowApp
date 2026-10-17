using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.PlatformAdmin;

namespace Omni.App.Persistence.Repositories.PlatformAdmin;

public class AdminUserRepository : IAdminUserRepository
{
    private readonly ApplicationDbContext _db;

    public AdminUserRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<AdminRow>> ListAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        return await _db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Admin)
            .OrderByDescending(u => u.IsActive).ThenBy(u => u.Email)
            .Select(u => new AdminRow(u.Id, u.Email, u.FullName, u.IsActive, u.LockoutEnd != null && u.LockoutEnd > now,
                u.LastLoginAt, u.CreatedAt))
            .ToListAsync(ct);
    }

    public Task<int> CountActiveAsync(CancellationToken ct = default) =>
        _db.Users.CountAsync(u => u.Role == UserRole.Admin && u.IsActive, ct);
}
