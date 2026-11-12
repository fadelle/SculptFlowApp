using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Omni.App.Controllers.Filters;

/// <summary>
/// Server-to-server auth for n8n's ingest endpoint — checks a shared-secret header against
/// N8n:IngestApiKey (set via user-secrets locally, an environment variable/secret store in
/// production). This is a placeholder proportional to the rest of this MVP (no real auth system
/// exists yet anywhere in the app — see IClientContext's doc comment), but it's a real, working
/// check: without a matching key, the request is rejected before any handler code runs. Replace
/// with a proper service-to-service auth scheme (mTLS, signed JWT, etc.) before going further than
/// local/trusted-network use.
/// It is an authorization filter (QA t4-ai-08 / t6-admin-08), so it runs before model binding and [ApiController]'s
/// validation: a caller without the key gets 401 (or 404), never a 400 that lists the request's fields.
/// </summary>
public class RequireIngestKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string HeaderName = "X-Ingest-Key";

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expected = configuration["N8n:IngestApiKey"];
        var provided = context.HttpContext.Request.Headers[HeaderName].FirstOrDefault();

        if (string.IsNullOrEmpty(expected))
        {
            context.Result = new ObjectResult(new { error = "Server is missing N8n:IngestApiKey configuration." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
            return Task.CompletedTask;
        }

        if (string.IsNullOrEmpty(provided) || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(provided), System.Text.Encoding.UTF8.GetBytes(expected)))
        {
            // Logged, so a probe or a leaked old key shows on the admin Errors page (QA t4-ai-09).
            context.HttpContext.RequestServices.GetService<ILogger<RequireIngestKeyAttribute>>()?.LogWarning(
                "Ingest call {Method} {Path} refused: {Problem} {Header} header from {Address}.", context.HttpContext.Request.Method,
                context.HttpContext.Request.Path.Value, string.IsNullOrEmpty(provided) ? "no" : "a wrong", HeaderName,
                context.HttpContext.Connection.RemoteIpAddress?.ToString());
            context.Result = new UnauthorizedObjectResult(new { error = $"Missing or invalid {HeaderName} header." });
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }
}
