using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PlasticSurgery.Controllers;

/// <summary>
/// Server-to-server auth for n8n's ingest endpoint — checks a shared-secret header against
/// N8n:IngestApiKey (set via user-secrets locally, an environment variable/secret store in
/// production). This is a placeholder proportional to the rest of this MVP (no real auth system
/// exists yet anywhere in the app — see IClinicContext's doc comment), but it's a real, working
/// check: without a matching key, the request is rejected before any handler code runs. Replace
/// with a proper service-to-service auth scheme (mTLS, signed JWT, etc.) before going further than
/// local/trusted-network use.
/// </summary>
public class RequireIngestKeyAttribute : Attribute, IAsyncActionFilter
{
    public const string HeaderName = "X-Ingest-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
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
            return;
        }

        if (string.IsNullOrEmpty(provided) || provided != expected)
        {
            context.Result = new UnauthorizedObjectResult(new { error = $"Missing or invalid {HeaderName} header." });
            return;
        }

        await next();
    }
}
