using Microsoft.Extensions.Localization;
using Omni.App.Common.Helpers;

namespace Omni.App.Middleware;

/// <summary>
/// The last net for the JSON APIs (QA t1-accounts-02): an exception nothing else turned into an answer ([ApiErrors] maps the
/// known ones) becomes a 500 with <c>{ "error": "...", "traceId": "..." }</c> in the request's language instead of the HTML
/// error page, so the dashboard's scripts can show a message and support can find the logged error by its trace id. Only for
/// paths under /api; a webhook still gets its 500, so the provider retries. The exception is logged as an error.
/// </summary>
public sealed class ApiExceptionMiddleware
{
    public const string TextKey = "common.errors.unexpected";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(ex, "Unhandled error on {Method} {Path}", context.Request.Method, context.Request.Path);
            if (context.Response.HasStarted) throw;

            var traceId = TraceContext.CurrentTraceId() ?? context.TraceIdentifier;
            var text = context.RequestServices?.GetService<IStringLocalizer>()?[TextKey].Value ?? "Something went wrong. Please try again."; // text-ok: fallback when no localizer is registered
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { error = text, traceId });
        }
    }
}
