using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts.Users;

namespace PlasticSurgery.Persistence.Repositories.Users;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _db;

    public UserRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<string>> ListEmailsAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default) =>
        await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => u.Email ?? string.Empty)
            .ToListAsync(ct);

    public Task DeleteAsync(IReadOnlyCollection<string> userIds, CancellationToken ct = default) =>
        _db.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync(ct);
}
