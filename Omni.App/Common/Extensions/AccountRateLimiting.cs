using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Configs;
using Omni.App.Common.Helpers;

namespace Omni.App.Common.Extensions;

/// <summary>
/// Caps how many account-form POSTs one visitor IP can make per hour, so a script can't mass-create clients, guess
/// passwords, keep an owner locked out or flood reset and invite links:
/// <list type="bullet">
/// <item>sign-up (Register, the Google client-name step): <c>Accounts:SignupsPerIpPerHour</c>;</item>
/// <item>sign-in, forgot password, reset password, accept invite: <c>Accounts:AttemptsPerIpPerHour</c>.</item>
/// </list>
/// Every other request is untouched, and so is a request carrying the platform-admin key (Omni.Automation registers and
/// signs in dozens of throwaway clients per run from one machine). The visitor IP is <see cref="ClientIp.Resolve"/>: the header the edge in front of the
/// app sets (<c>Proxy:ClientIpHeader</c>, CF-Connecting-IP on Render), else the connection's address. The limit is part
/// of the partition key, so changing a setting applies straight away. A refused attempt gets 429 with a short plain-text
/// explanation (the request's language isn't known yet at this point of the pipeline).
/// </summary>
public static class AccountRateLimiting
{
    private static readonly PathString[] SignupPaths = ["/Account/Register", "/Account/CompleteGoogleSignup"];

    private static readonly PathString[] AttemptPaths =
        ["/Account/Login", "/Account/ForgotPassword", "/Account/ResetPassword", "/Account/AcceptInvite"];

    public const string RejectedMessage = "Too many sign-up attempts from your network. Please wait an hour and try again."; // text-ok: written before the request's language is known

    public const string AttemptsRejectedMessage = "Too many attempts from your network. Please wait an hour and try again."; // text-ok: written before the request's language is known

    public static IServiceCollection AddAccountRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(Partition);
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                await context.HttpContext.Response.WriteAsync(
                    IsSignupAttempt(context.HttpContext.Request) ? RejectedMessage : AttemptsRejectedMessage, ct);
            };
        });

    /// <summary>The limiter for one request: a per-IP hourly window for an account-form POST, no limit for anything else.</summary>
    public static RateLimitPartition<string> Partition(HttpContext context)
    {
        var signup = IsSignupAttempt(context.Request);
        if (!signup && !IsAccountAttempt(context.Request)) return RateLimitPartition.GetNoLimiter("unlimited");
        if (context.RequestServices.GetService<IConfiguration>() is { } configuration && PlatformAdminKey.Matches(context.Request, configuration))
        {
            return RateLimitPartition.GetNoLimiter("platform-admin");
        }

        var config = context.RequestServices.GetRequiredService<IConfigManager>();
        var limit = signup ? config.AccountsSignupsPerIpPerHour : config.AccountsAttemptsPerIpPerHour;
        var header = context.RequestServices.GetService<IOptions<ProxyOptions>>()?.Value.ClientIpHeader;
        var ip = ClientIp.Resolve(context, header);
        return RateLimitPartition.GetFixedWindowLimiter($"{(signup ? "signup" : "account")}:{limit}:{ip}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    public static bool IsSignupAttempt(HttpRequest request) => IsPostTo(request, SignupPaths);

    /// <summary>Sign-in, forgot password, reset password and accept invite (not sign-up, which has its own limit).</summary>
    public static bool IsAccountAttempt(HttpRequest request) => IsPostTo(request, AttemptPaths);

    private static bool IsPostTo(HttpRequest request, PathString[] paths) =>
        HttpMethods.IsPost(request.Method)
        && paths.Any(p => request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
}
