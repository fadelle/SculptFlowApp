using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Omni.App.Common.Exceptions;
using Omni.App.Controllers.Filters;

namespace Omni.App.Tests;

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
        { new EmailSendException("Brevo refused"), 502 },
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
    public void A_booking_rule_the_member_may_override_answers_422_with_its_code()
    {
        // QA t5-ops-01: the booking dialog asks "Book anyway?" on this code.
        var context = new ExceptionContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [])
        {
            Exception = new BookingRulesException("bookings.rules.closed") { Reason = SlotCheckReason.Closed }
        };

        new ApiErrorsAttribute().OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(422, result.StatusCode);
        Assert.Equal("booking_rules", result.Value!.GetType().GetProperty("code")!.GetValue(result.Value));
        Assert.Equal(409, ApiErrorsAttribute.StatusFor(new OverlappingRecordException("bookings.errors.overlap")));
    }

    [Fact]
    public void An_email_failure_answers_a_fixed_text_never_the_providers_reason()
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(TextKit.Localizer()).BuildServiceProvider()
        };
        var context = new ExceptionContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [])
        {
            Exception = new EmailSendException("Brevo refused the email (HTTP 401): unauthorized: Key not found")
        };

        new ApiErrorsAttribute().OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(502, result.StatusCode);
        Assert.Equal("We couldn't send the email right now. Please try again in a few minutes.", result.Value!.GetType().GetProperty("error")!.GetValue(result.Value));
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
