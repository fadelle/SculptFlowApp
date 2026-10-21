using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Users;

namespace Omni.App.Persistence.Repositories.Users;

public class AccountTokenRepository : IAccountTokenRepository
{
    private readonly ApplicationDbContext _db;

    public AccountTokenRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public void Add(AccountToken token) => _db.AccountTokens.Add(token);

    public Task<AccountToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default) =>
        _db.AccountTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public Task<int> CountForUserSinceAsync(string purpose, string userId, DateTimeOffset since, CancellationToken ct = default) =>
        _db.AccountTokens.AsNoTracking().CountAsync(t => t.Purpose == purpose && t.UserId == userId && t.CreatedAt >= since, ct);

    public async Task<IReadOnlyList<AccountToken>> ListOpenForUserAsync(string purpose, string userId, DateTimeOffset now, CancellationToken ct = default) =>
        await _db.AccountTokens
            .Where(t => t.Purpose == purpose && t.UserId == userId && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);
}
