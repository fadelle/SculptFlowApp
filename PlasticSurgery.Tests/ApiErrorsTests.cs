using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Controllers.Filters;

namespace PlasticSurgery.Tests;

/// <summary>The one error mapping the JSON APIs share (ApiErrorsAttribute).</summary>
public class ApiErrorsTests
{
    public static TheoryData<Exception, int?> Cases => new()
    {
        { new ArgumentException("bad"), 400 },
        { new ArgumentNullException("x"), 400 },
        { new KeyNotFoundException(), 404 },
        { new InvalidOperationException("no"), 422 },
        { new BenchmarkRunInProgressException(), 409 },
        { new DuplicateRecordException(new Exception()), 409 },
        { new BenchmarkGeneratorNotConfiguredException("off"), 503 },
        { new ChannelSendException("down"), 502 },
        { new NullReferenceException(), null }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Maps_each_exception_to_its_status(Exception exception, int? expected) =>
        Assert.Equal(expected, ApiErrorsAttribute.StatusFor(exception));

    [Fact]
    public void Writes_the_message_as_error_and_marks_it_handled()
    {
        var context = new ExceptionContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [])
        {
            Exception = new InvalidOperationException("Your plan doesn't include campaigns.")
        };

        new ApiErrorsAttribute().OnException(context);

        Assert.True(context.ExceptionHandled);
        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(422, result.StatusCode);
        Assert.Equal("Your plan doesn't include campaigns.", result.Value!.GetType().GetProperty("error")!.GetValue(result.Value));
    }

    [Fact]
    public void Leaves_unexpected_exceptions_to_become_a_500()
    {
        var context = new ExceptionContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [])
        {
            Exception = new NullReferenceException()
        };

        new ApiErrorsAttribute().OnException(context);

        Assert.False(context.ExceptionHandled);
        Assert.Null(context.Result);
    }
}
