using Microsoft.EntityFrameworkCore;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Caching;
using Omni.App.Persistence.Helpers;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Users;

namespace Omni.App.Persistence.Repositories.Users;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _db;
    private readonly ICacheManager? _cache;

    public UserRepository(ApplicationDbContext db, ICacheManager? cache = null)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<IReadOnlyList<string>> ListEmailsAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default) =>
        await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => u.Email ?? string.Empty)
            .ToListAsync(ct);

    public async Task DeleteAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default)
    {
        await _db.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync(ct);
        // The delete cascades to their client memberships.
        await _cache.RemoveIfCachedAsync(userIds.Select(id => CacheEviction.Key(CacheKeys.Membership(id))), ct);
    }
}
