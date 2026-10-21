using Microsoft.EntityFrameworkCore;
using Omni.App.Common.Enums;
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

    public async Task<IReadOnlyList<AccountToken>> ListPendingInvitesAsync(Guid clientId, CancellationToken ct = default) =>
        await _db.AccountTokens.AsNoTracking()
            .Where(t => t.Purpose == AccountTokenPurpose.Invite && t.ClientId == clientId && t.UsedAt == null && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    public Task<AccountToken?> GetInviteAsync(Guid clientId, Guid inviteId, CancellationToken ct = default) =>
        _db.AccountTokens.FirstOrDefaultAsync(t => t.Id == inviteId && t.ClientId == clientId && t.Purpose == AccountTokenPurpose.Invite, ct);

    public Task<AccountToken?> GetInviteAsync(Guid inviteId, CancellationToken ct = default) =>
        _db.AccountTokens.FirstOrDefaultAsync(t => t.Id == inviteId && t.Purpose == AccountTokenPurpose.Invite, ct);

    public Task<int> CountOpenInvitesAsync(Guid clientId, DateTimeOffset now, Guid? exceptInviteId = null, CancellationToken ct = default) =>
        OpenInvites(clientId, now).CountAsync(t => exceptInviteId == null || t.Id != exceptInviteId, ct);

    public Task<bool> HasOpenInviteAsync(Guid clientId, string email, DateTimeOffset now, CancellationToken ct = default)
    {
        var lower = email.ToLowerInvariant();
        return OpenInvites(clientId, now).AnyAsync(t => t.Email.ToLower() == lower, ct);
    }

    private IQueryable<AccountToken> OpenInvites(Guid clientId, DateTimeOffset now) =>
        _db.AccountTokens.AsNoTracking().Where(t => t.Purpose == AccountTokenPurpose.Invite && t.ClientId == clientId
            && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now);
}
