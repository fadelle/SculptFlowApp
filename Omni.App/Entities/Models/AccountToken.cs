namespace Omni.App.Entities.Models;

/// <summary>
/// A one-time link sent by email (identity.account_tokens): a password reset (UserId set) or a member invite (ClientId,
/// Email and the invited name; UserId stays empty until it is accepted). Only the SHA-256 hash of the link's token is
/// stored (AccountTokenCodec). A link works once, until ExpiresAt, and never after RevokedAt.
/// </summary>
public class AccountToken
{
    public Guid Id { get; set; }

    /// <summary>AccountTokenPurpose value.</summary>
    public string Purpose { get; set; } = string.Empty;

    public string? UserId { get; set; }
    public Guid? ClientId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }

    /// <summary>Lowercase hex SHA-256 of the raw token in the link.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public string? CreatedByUserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Not used, not revoked and not expired at <paramref name="now"/>.</summary>
    public bool IsOpen(DateTimeOffset now) => UsedAt is null && RevokedAt is null && now < ExpiresAt;
}
