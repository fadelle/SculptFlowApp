using Microsoft.Extensions.Configuration;
using Omni.App.Common.Helpers;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Omni.App.Controllers.Client;
using Omni.App.Controllers.Filters;
using Omni.App.Controllers.Integrations;

namespace Omni.App.Tests.Controllers;

public class BotToolAttributeTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _botId = Guid.NewGuid();
    private readonly Mock<IBotRuntimeService> _runtime = new();

    private async Task<(bool Ran, IActionResult? Result, HttpContext Http)> RunAsync(BotToolAttribute filter, Dictionary<string, object?> arguments,
        Action<HttpRequest>? request = null, string? runTokenSecret = null)
    {
        var services = new ServiceCollection().AddSingleton(_runtime.Object);
        if (runTokenSecret is not null)
        {
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [AiRunToken.SecretKey] = runTokenSecret }).Build());
        }
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        request?.Invoke(http.Request);
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), arguments, new object());
        var ran = false;
        await filter.OnActionExecutionAsync(executing, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        });
        return (ran, executing.Result, http);
    }

    [Fact]
    public async Task An_allowed_call_runs_with_the_bots_run_on_the_request()
    {
        var session = Guid.NewGuid();
        var run = new BotRunContext(_clientId, _botId, new HashSet<string> { BotCapability.Bookings }, null, null, session);
        _runtime.Setup(r => r.AuthorizeAsync(_clientId, _botId, session, BotCapability.Bookings, It.IsAny<CancellationToken>())).ReturnsAsync(BotToolCheck.Allowed(run));

        var (ran, result, http) = await RunAsync(new BotToolAttribute(BotCapability.Bookings), new() { ["clientId"] = _clientId }, r =>
        {
            r.Headers[BotToolAttribute.BotHeader] = _botId.ToString();
            r.Headers[BotToolAttribute.TestSessionHeader] = session.ToString();
        });

        Assert.True(ran);
        Assert.Null(result);
        Assert.Same(run, BotToolAttribute.RunOf(http));
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("other-client", false)]
    [InlineData("valid", true)]
    public async Task With_a_run_token_secret_a_call_needs_this_runs_token(string token, bool runs)
    {
        // QA t4-ai-09: the ingest key alone was enough to act for any client.
        var run = new BotRunContext(_clientId, _botId, new HashSet<string>(), null, null, null);
        _runtime.Setup(r => r.AuthorizeAsync(_clientId, _botId, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(BotToolCheck.Allowed(run));
        var value = token switch
        {
            "valid" => AiRunToken.Issue("s", _clientId, _botId, DateTimeOffset.UtcNow),
            "other-client" => AiRunToken.Issue("s", Guid.NewGuid(), _botId, DateTimeOffset.UtcNow),
            _ => null
        };

        var (ran, result, _) = await RunAsync(new BotToolAttribute(), new() { ["clientId"] = _clientId }, r =>
        {
            r.Headers[BotToolAttribute.BotHeader] = _botId.ToString();
            if (value is not null) r.Headers[AiRunToken.Header] = value;
        }, runTokenSecret: "s");

        Assert.Equal(runs, ran);
        if (!runs) Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task A_refused_call_answers_tool_not_allowed_without_running_the_action()
    {
        _runtime.Setup(r => r.AuthorizeAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BotToolCheck.Denied(BotToolCheck.NotAllowed));

        var (ran, result, http) = await RunAsync(new BotToolAttribute(BotCapability.Knowledge), new() { ["request"] = new KnowledgeSearchRequest(_clientId, "q") },
            r => r.Headers[BotToolAttribute.BotHeader] = "not-a-guid");

        Assert.False(ran);
        var body = JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Contains("\"code\":\"TOOL_NOT_ALLOWED\"", body);
        Assert.Contains("\"success\":false", body);
        // The client came from the body; a bad or missing header names no bot.
        _runtime.Verify(r => r.AuthorizeAsync(_clientId, null, null, BotCapability.Knowledge, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Throws<InvalidOperationException>(() => BotToolAttribute.RunOf(http));
    }

    [Fact]
    public async Task Without_any_client_id_the_runtime_is_asked_with_none()
    {
        _runtime.Setup(r => r.AuthorizeAsync(Guid.Empty, null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(BotToolCheck.Denied(BotToolCheck.UnknownBot));
        var (ran, _, _) = await RunAsync(new BotToolAttribute(), new());
        Assert.False(ran);
    }

    // QA t9-01: the live n8n workflow sent no clientId and no bot header; every tool answered 200 and nothing ever looked wrong.
    [Fact]
    public async Task A_call_without_the_client_or_the_bot_is_a_400_so_the_run_fails_visibly()
    {
        _runtime.Setup(r => r.AuthorizeAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BotToolCheck.Denied(BotToolCheck.MissingContext));

        var (ran, result, _) = await RunAsync(new BotToolAttribute(BotCapability.Knowledge), new());

        Assert.False(ran);
        var body = JsonSerializer.Serialize(Assert.IsType<BadRequestObjectResult>(result).Value);
        Assert.Contains($"\"code\":\"{BotToolOutcomes.MissingContextCode}\"", body);
        Assert.Contains("X-Omni-Bot-Id", body);
    }

    [Theory]
    [InlineData(BotToolCheck.UnknownBot)]
    [InlineData(BotToolCheck.BotNotActive)]
    public async Task A_bot_that_may_no_longer_answer_is_a_403_like_the_code_engine(string reason)
    {
        _runtime.Setup(r => r.AuthorizeAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BotToolCheck.Denied(reason));

        var (ran, result, _) = await RunAsync(new BotToolAttribute(), new() { ["clientId"] = _clientId }, r => r.Headers[BotToolAttribute.BotHeader] = Guid.NewGuid().ToString());

        Assert.False(ran);
        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Contains($"\"code\":\"{BotToolOutcomes.BotNotAllowedCode}\"", JsonSerializer.Serialize(forbidden.Value));
    }

    [Fact]
    public async Task The_runtime_calls_a_missing_client_or_bot_missing_context()
    {
        var bots = new Mock<IBotRepository>();
        var runtime = new Omni.App.Business.Services.Ai.BotRuntimeService(bots.Object, Mock.Of<IBotTestRepository>(), Mock.Of<IEntitlementService>());
        Assert.Equal(BotToolCheck.MissingContext, (await runtime.AuthorizeAsync(Guid.Empty, Guid.NewGuid(), null, null)).DeniedReason);
        Assert.Equal(BotToolCheck.MissingContext, (await runtime.AuthorizeAsync(_clientId, null, null, null)).DeniedReason);
        bots.Verify(b => b.ListForClientReadOnlyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Every_ai_tool_action_is_a_bot_tool_and_runs_before_the_feature_check()
    {
        var actions = typeof(AiController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any()).ToList();
        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.NotNull(a.GetCustomAttribute<BotToolAttribute>()));
        Assert.True(new BotToolAttribute().Order < 0);
        Assert.True(new TraceBotToolAttribute().Order < new BotToolAttribute().Order); // refused calls are traced too

        string? Capability(string action) => typeof(AiController).GetMethod(action)!.GetCustomAttribute<BotToolAttribute>()!.Capability;
        Assert.Equal((BotCapability.Knowledge, BotCapability.Services, BotCapability.Availability, BotCapability.Bookings, BotCapability.Bookings, BotCapability.Bookings),
            (Capability(nameof(AiController.SearchKnowledge)), Capability(nameof(AiController.GetServices)), Capability(nameof(AiController.GetAvailableSlots)),
             Capability(nameof(AiController.ScheduleBooking)), Capability(nameof(AiController.CancelBooking)), Capability(nameof(AiController.GetMyBookings))));
        Assert.Null(Capability(nameof(AiController.HandoffToHuman)));
        Assert.Null(Capability(nameof(AiController.Reply)));
    }
}

public class BotTestControllerTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _botId = Guid.NewGuid();
    private readonly Mock<IBotTestService> _tests = new();
    private readonly Mock<ICurrentClientContext> _context = new();

    public BotTestControllerTests() =>
        _context.Setup(c => c.GetClientIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_clientId);

    private BotTestController Sut() => new BotTestController(_tests.Object, TextKit.Localizer(), _context.Object).With();

    [Fact]
    public async Task Send_read_and_reset_use_the_signed_in_client()
    {
        var session = new BotTestSession { Id = Guid.NewGuid(), ClientId = _clientId, BotId = _botId };
        _tests.Setup(t => t.SendAsync(_clientId, _botId, null, "Hi", It.IsAny<CancellationToken>())).ReturnsAsync(session.Id);
        Assert.Contains(session.Id.ToString(), JsonSerializer.Serialize(Assert.IsType<OkObjectResult>((await Sut().Send(_botId, new SendBotTestMessageRequest(null, "Hi"), default)).Result).Value));

        var note = JsonSerializer.Serialize(BotTestNote.Of(BotTestNote.BookedKey, "Monday, Jan 5 at 2:00 PM"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        _tests.Setup(t => t.GetAsync(_clientId, _botId, session.Id, It.IsAny<CancellationToken>())).ReturnsAsync((session,
            (IReadOnlyList<BotTestMessage>)[new BotTestMessage { Role = BotTestRole.Contact, Content = "Hi" }, new BotTestMessage { Role = BotTestRole.Note, Content = note }]));
        var transcript = (BotTestTranscriptDto)Assert.IsType<OkObjectResult>((await Sut().Get(_botId, session.Id, default)).Result).Value!;
        Assert.Equal("Simulated: booked Monday, Jan 5 at 2:00 PM", transcript.Lines[1].Text);
        Assert.IsType<NotFoundResult>((await Sut().Get(_botId, Guid.NewGuid(), default)).Result);

        Assert.IsType<NoContentResult>(await Sut().Reset(_botId, session.Id, default));
        _tests.Verify(t => t.ResetAsync(_clientId, _botId, session.Id, It.IsAny<CancellationToken>()), Times.Once);

        _context.Setup(c => c.GetClientIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        Assert.IsType<ForbidResult>((await Sut().Send(_botId, new SendBotTestMessageRequest(null, "Hi"), default)).Result);
        Assert.IsType<ForbidResult>((await Sut().Get(_botId, session.Id, default)).Result);
        Assert.IsType<ForbidResult>(await Sut().Reset(_botId, session.Id, default));
    }
}
