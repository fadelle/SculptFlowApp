using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Middleware;

namespace Omni.App.Tests.Middleware;

/// <summary>QA t1-accounts-02: an unhandled error on a JSON API answered the HTML "Something went wrong" page.</summary>
public class ApiExceptionMiddlewareTests
{
    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "req-1" };
        context.Request.Path = "/api/contacts";
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task An_unhandled_error_answers_a_json_500_with_the_trace_id()
    {
        var context = Context();
        var middleware = new ApiExceptionMiddleware(_ => throw new InvalidCastException("boom"), NullLogger<ApiExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("error").GetString()));
        Assert.DoesNotContain("boom", body.RootElement.GetProperty("error").GetString());   // never the exception's own text
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task A_request_that_works_or_a_response_already_sent_is_left_alone()
    {
        var context = Context();
        await new ApiExceptionMiddleware(c => { c.Response.StatusCode = 204; return Task.CompletedTask; }, NullLogger<ApiExceptionMiddleware>.Instance)
            .InvokeAsync(context);
        Assert.Equal(204, context.Response.StatusCode);
    }
}
