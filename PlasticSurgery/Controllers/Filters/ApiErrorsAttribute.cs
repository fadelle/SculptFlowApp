using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PlasticSurgery.Common.Exceptions;

namespace PlasticSurgery.Controllers.Filters;

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
/// Anything else stays a 500. A controller catches an exception itself only when its response must differ from this
/// (an n8n tool contract, a redirect, a different status for that one action).
/// Applied to the clinic dashboard APIs (DashboardApiController and ConversationsController), the platform-admin APIs
/// and the n8n/AI integration APIs. Not to provider webhooks, which must keep answering 500 so the provider retries.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ApiErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (StatusFor(context.Exception) is not { } status) return;
        context.Result = new ObjectResult(new { error = context.Exception.Message }) { StatusCode = status };
        context.ExceptionHandled = true;
    }

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
