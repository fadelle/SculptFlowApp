using System.Security.Cryptography;
using System.Text;

namespace Omni.App.Common.Helpers;

/// <summary>The platform-admin shared secret (PlatformAdmin:ApiKey, sent as X-Platform-Admin-Key), compared in constant time.
/// Used by [RequirePlatformAdminKey] and by the account rate limits (Omni.Automation's test sign-ups carry it).</summary>
public static class PlatformAdminKey
{
    public const string ConfigKey = "PlatformAdmin:ApiKey";
    public const string HeaderName = "X-Platform-Admin-Key";

    /// <summary>True when a key is configured and the request carries exactly it.</summary>
    public static bool Matches(HttpRequest request, IConfiguration configuration)
    {
        var expected = configuration[ConfigKey];
        if (string.IsNullOrEmpty(expected)) return false;
        var provided = request.Headers[HeaderName].FirstOrDefault() ?? string.Empty;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
    }
}
