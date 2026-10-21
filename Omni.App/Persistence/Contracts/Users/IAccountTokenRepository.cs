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
}
