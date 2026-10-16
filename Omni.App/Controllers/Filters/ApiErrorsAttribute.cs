using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Localization;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;

namespace Omni.App.Controllers.Filters;

/// <summary>
/// The one place that turns an exception thrown by a service into an HTTP response for the JSON APIs, so controllers
/// don't need their own try/catch. The body is always <c>{ "error": "message" }</c>:
/// <list type="bullet">
/// <item>400: <see cref="ArgumentException"/> (invalid input).</item>
/// <item>404: <see cref="KeyNotFoundException"/>.</item>
/// <item>409: a duplicate or overlapping record, the WhatsApp 24-hour window is closed, a benchmark run or question
/// generation is already in progress.</item>
/// <item>422: <see cref="InvalidOperationException"/> (a business rule says no: plan limit, wallet empty, wrong state).</item>
/// <item>502: the provider refused or failed (channel send, Meta Graph API, benchmark question generator).</item>
/// <item>503: the benchmark question generator isn't configured.</item>
/// </list>
/// Anything else stays a 500. The message is put into the request's language: a service throws with a strings-file key
/// (and its arguments, see <see cref="TextError"/>) and the key's text is answered; a message that isn't a key is answered
/// as written. A controller catches an exception itself only when its response must differ from this
/// (an n8n tool contract, a redirect, a different status for that one action).
/// A 502/503 is also logged as an error (it is a provider or setup failure, not the caller's mistake), so it reaches the
/// error log; 4xx answers are not logged.
/// Applied to the client dashboard APIs (DashboardApiController and ConversationsController), the platform-admin APIs
/// and the n8n/AI integration APIs. Not to provider webhooks, which must keep answering 500 so the provider retries.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ApiErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (StatusFor(context.Exception) is not { } status) return;
        if (status >= StatusCodes.Status500InternalServerError)
        {
            context.HttpContext.RequestServices?.GetService<ILoggerFactory>()?.CreateLogger<ApiErrorsAttribute>()
                .LogError(context.Exception, "{Action} answered {StatusCode}: {Error}",
                    context.ActionDescriptor.DisplayName, status, context.Exception.Message);
        }
        context.Result = new ObjectResult(new { error = Localize(context.HttpContext, context.Exception) }) { StatusCode = status };
        context.ExceptionHandled = true;
    }

    /// <summary>The exception's message in the request's language when it is a strings-file key, else as written.</summary>
    public static string Localize(HttpContext http, Exception exception) =>
        http.RequestServices?.GetService<IStringLocalizer>().Localize(exception) ?? exception.Message;

    /// <summary>The HTTP status for an exception, or null to let it become a 500. Order matters: specific types first,
    /// because some of them derive from InvalidOperationException.</summary>
    public static int? StatusFor(Exception exception) => exception switch
    {
        BenchmarkRunInProgressException or BenchmarkGenerationInProgressException => StatusCodes.Status409Conflict,
        ServiceWindowClosedException or DuplicateRecordException or OverlappingRecordException => StatusCodes.Status409Conflict,
        BenchmarkGeneratorNotConfiguredException => StatusCodes.Status503ServiceUnavailable,
        ChannelSendException or MetaGraphApiException or BenchmarkGenerationException => StatusCodes.Status502BadGateway,
        KeyNotFoundException => StatusCodes.Status404NotFound,
        ArgumentException => StatusCodes.Status400BadRequest,
        InvalidOperationException => StatusCodes.Status422UnprocessableEntity,
        _ => null
    };
}
