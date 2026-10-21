using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Users;

/// <summary>One-time emailed links (identity.account_tokens): password resets and member invites. Looked up by the hash
/// of the link's token only (AccountTokenCodec).</summary>
public interface IAccountTokenRepository
{
    void Add(AccountToken token);

    /// <summary>The token with this hash (tracked, so the caller can mark it used), or null.</summary>
    Task<AccountToken?> GetByHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>How many links of this purpose were made for this user since <paramref name="since"/> (rate limit).</summary>
    Task<int> CountForUserSinceAsync(string purpose, string userId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>The user's links of this purpose that still work at <paramref name="now"/> (tracked).</summary>
    Task<IReadOnlyList<AccountToken>> ListOpenForUserAsync(string purpose, string userId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>A client's invites that were neither accepted nor revoked (expired ones included), newest first, read-only.</summary>
    Task<IReadOnlyList<AccountToken>> ListPendingInvitesAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>One invite of this client (tracked), or null.</summary>
    Task<AccountToken?> GetInviteAsync(Guid clientId, Guid inviteId, CancellationToken ct = default);

    /// <summary>One invite by id whatever its client (tracked; the admin portal), or null.</summary>
    Task<AccountToken?> GetInviteAsync(Guid inviteId, CancellationToken ct = default);

    /// <summary>Invites of this client that still hold a seat at <paramref name="now"/> (not accepted, revoked or expired),
    /// leaving out <paramref name="exceptInviteId"/>.</summary>
    Task<int> CountOpenInvitesAsync(Guid clientId, DateTimeOffset now, Guid? exceptInviteId = null, CancellationToken ct = default);

    /// <summary>Whether this client already has an open invite for this email (case-insensitive).</summary>
    Task<bool> HasOpenInviteAsync(Guid clientId, string email, DateTimeOffset now, CancellationToken ct = default);
}
