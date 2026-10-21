using System.Security.Cryptography;
using System.Text;

namespace Omni.App.Common.Helpers;

/// <summary>The raw token of an emailed link (32 random bytes, base64url, 43 characters) and the hash stored for it
/// (identity.account_tokens.token_hash). The raw token only ever exists in the email and the link.</summary>
public static class AccountTokenCodec
{
    public const int TokenBytes = 32;

    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>Lowercase hex SHA-256 of the token as it appears in the link; null for an empty or oversized value.</summary>
    public static string? Hash(string? token)
    {
        var trimmed = token?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 200) return null;
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(trimmed)));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
