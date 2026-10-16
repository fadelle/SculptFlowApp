using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Omni.App.Business.Contracts.Managers;

namespace Omni.App.Common.Extensions;

/// <summary>
/// Caps how many sign-up attempts one IP address can make per hour (setting Accounts:SignupsPerIpPerHour), so a script
/// can't mass-create clinics. Only POSTs to the two pages that create a clinic count (Register and the Google
/// clinic-name step); every other request is untouched. The client IP comes from X-Forwarded-For (Render's proxy),
/// which Program.cs trusts through UseForwardedHeaders. The limit is part of the partition key, so changing the
/// setting applies straight away. A refused attempt gets 429 with a short plain-text explanation.
/// </summary>
public static class SignupRateLimiting
{
    private static readonly PathString[] SignupPaths = ["/Account/Register", "/Account/CompleteGoogleSignup"];

    public const string RejectedMessage = "Too many sign-up attempts from your network. Please wait an hour and try again.";

    public static IServiceCollection AddSignupRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(Partition);
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                await context.HttpContext.Response.WriteAsync(RejectedMessage, ct);
            };
        });

    /// <summary>The limiter for one request: a per-IP hourly window for a sign-up POST, no limit for anything else.</summary>
    public static RateLimitPartition<string> Partition(HttpContext context)
    {
        if (!IsSignupAttempt(context.Request)) return RateLimitPartition.GetNoLimiter("unlimited");

        var limit = context.RequestServices.GetRequiredService<IConfigManager>().AccountsSignupsPerIpPerHour;
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"signup:{limit}:{ip}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    public static bool IsSignupAttempt(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && SignupPaths.Any(p => request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
}
