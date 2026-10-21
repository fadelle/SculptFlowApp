namespace Omni.App.Common.Helpers;

/// <summary>Absolute links put in account emails. The origin is App:PublicBaseUrl when it is set (always on Render), so a
/// forged Host header can never point a reset link elsewhere; otherwise the address the request came in on (local runs).</summary>
public static class AccountLinks
{
    public const string ResetPasswordPath = "/Account/ResetPassword";
    public const string AcceptInvitePath = "/Account/AcceptInvite";

    public static string Origin(string? publicBaseUrl, string requestBaseUrl)
    {
        var configured = publicBaseUrl?.Trim().TrimEnd('/');
        return string.IsNullOrEmpty(configured) ? requestBaseUrl.TrimEnd('/') : configured;
    }

    public static string Build(string origin, string path, string token) => $"{origin}{path}?token={Uri.EscapeDataString(token)}";
}
