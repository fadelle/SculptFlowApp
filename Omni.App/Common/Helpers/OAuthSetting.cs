namespace Omni.App.Common.Helpers;

/// <summary>
/// An OAuth app's settings (client id / key and secret). A blank value counts as missing (QA t5-ops-13: appsettings ship
/// "ClientId": "", and members were sent to Microsoft with client_id= and an error page).
/// </summary>
public static class OAuthSetting
{
    /// <summary>The value; InvalidOperationException (the "isn't set up on this server" message) when missing or blank.</summary>
    public static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { } value && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidOperationException($"Missing {key} configuration."); // text-ok: shown inside oauth.errors.notSetUp

    /// <summary>Whether every key has a value.</summary>
    public static bool Has(IConfiguration configuration, params string[] keys) =>
        keys.All(k => !string.IsNullOrWhiteSpace(configuration[k]));
}
