using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Omni.App.Business.Services.Ai;
using Omni.App.Controllers.Filters;
using Omni.App.Controllers.Integrations;
using Omni.App.Entities.Dtos.Monitoring;

namespace Omni.App.Tests.Services;

/// <summary>IBotToolService.CallAsync: the bot's check, the plan's feature, the tool and the trace step, in the order and
/// with the answers the /api/ai filters give.</summary>
public class BotToolServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _botId = Guid.NewGuid();
    private readonly Guid _conversationId = Guid.NewGuid();
    private readonly Guid _contactId = Guid.NewGuid();
    private readonly Mock<IBotRuntimeService> _runtime = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly Mock<IMessageTracer> _tracer = new();

    private BotToolService Sut() => new(Mock.Of<IClientContext>(), Mock.Of<ICatalogService>(), Mock.Of<IContactService>(),
        Mock.Of<IBookingService>(), Mock.Of<IAvailabilityService>(), Mock.Of<IKnowledgeSearchService>(), _runtime.Object,
        Mock.Of<IBotTestService>(), Mock.Of<IBotConversationService>(), _entitlements.Object, _tracer.Object);

    private BotToolCall Call(Guid? testSession = null) => new(_clientId, _botId, testSession, _conversationId, _contactId);

    private BotRunContext Allow(string? capability, Guid? testSession = null)
    {
        var run = new BotRunContext(_clientId, _botId, new HashSet<string>(BotCapability.All), null, null, testSession);
        _runtime.Setup(r => r.AuthorizeAsync(_clientId, _botId, testSession, capability, It.IsAny<CancellationToken>())).ReturnsAsync(BotToolCheck.Allowed(run));
        return run;
    }

    private static string Json(object? body) => JsonSerializer.Serialize(body);

    [Fact]
    public async Task An_allowed_call_runs_the_tool_with_the_run_and_traces_an_ok_step()
    {
        var session = Guid.NewGuid();
        var run = Allow(BotCapability.Bookings, session);
        BotRunContext? seen = null;

        var result = await Sut().CallAsync(Call(session), BotTools.ScheduleBooking, (r, _) =>
        {
            seen = r;
            return Task.FromResult(BotToolResult.Ok(new { success = true }));
        });

        Assert.Same(run, seen);
        Assert.Equal(200, result.Status);
        _entitlements.Verify(e => e.EnsureFeatureAsync(_clientId, EntitlementKeys.Bookings, It.IsAny<CancellationToken>()), Times.Once);
        _tracer.Verify(t => t.Step(TraceSteps.BotToolCalled, TraceOutcomes.Ok,
            It.Is<TraceIds>(i => i.ClientId == _clientId && i.ConversationId == _conversationId), It.IsAny<double?>(),
            It.Is<object?>(d => Json(d).Contains("\"tool\":\"api/ai/bookings/schedule\"") && Json(d).Contains("\"status\":200") && Json(d).Contains(_contactId.ToString()))), Times.Once);
    }

    [Fact]
    public async Task A_refused_bot_answers_tool_not_allowed_before_the_plan_is_checked()
    {
        _runtime.Setup(r => r.AuthorizeAsync(_clientId, _botId, null, BotCapability.Bookings, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BotToolCheck.Denied(BotToolCheck.NotAllowed));
        var ran = false;

        var result = await Sut().CallAsync(Call(), BotTools.CancelBooking, (_, _) => { ran = true; return Task.FromResult(BotToolResult.Ok(null)); });

        Assert.False(ran);
        Assert.Equal(200, result.Status);
        Assert.Equal(Json(BotToolAttribute.NotAllowed(BotToolCheck.NotAllowed)), Json(result.Body));
        _entitlements.Verify(e => e.EnsureFeatureAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _tracer.Verify(t => t.Step(TraceSteps.BotToolCalled, TraceOutcomes.Ok, It.IsAny<TraceIds>(), It.IsAny<double?>(), It.IsAny<object?>()), Times.Once);
    }

    [Fact]
    public async Task A_plan_without_bookings_answers_feature_not_included_like_the_filter()
    {
        Allow(BotCapability.Availability);
        _entitlements.Setup(e => e.EnsureFeatureAsync(_clientId, EntitlementKeys.Bookings, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntitlementDeniedException(EntitlementKeys.Bookings, "no"));
        var ran = false;

        var result = await Sut().CallAsync(Call(), BotTools.AvailableSlots, (_, _) => { ran = true; return Task.FromResult(BotToolResult.Ok(null)); });

        Assert.False(ran);
        Assert.Contains("\"code\":\"FEATURE_NOT_INCLUDED\"", Json(result.Body));
        Assert.Equal(Json(BotToolOutcomes.FeatureNotIncluded(EntitlementKeys.Bookings)), Json(result.Body));
    }

    [Fact]
    public async Task Tools_without_a_feature_skip_the_plan_check_and_error_answers_and_exceptions_are_failed_steps()
    {
        Allow(null);
        var notFound = await Sut().CallAsync(Call(), BotTools.Handoff, (_, _) => Task.FromResult(BotToolResult.NotFound()));
        Assert.Equal(404, notFound.Status);
        _entitlements.Verify(e => e.EnsureFeatureAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().CallAsync(Call(), BotTools.Reply, (_, _) => throw new KeyNotFoundException()));
        _tracer.Verify(t => t.Step(TraceSteps.BotToolCalled, TraceOutcomes.Failed, It.IsAny<TraceIds>(), It.IsAny<double?>(),
            It.Is<object?>(d => Json(d).Contains("\"status\":404"))), Times.Once);
        _tracer.Verify(t => t.Step(TraceSteps.BotToolCalled, TraceOutcomes.Failed, It.IsAny<TraceIds>(), It.IsAny<double?>(),
            It.Is<object?>(d => Json(d).Contains("\"status\":500"))), Times.Once);
    }

    [Fact]
    public void BotTools_match_the_routes_capabilities_and_features_of_AiController()
    {
        var prefix = typeof(AiController).GetCustomAttribute<RouteAttribute>()!.Template;
        BotToolDefinition Of(string action)
        {
            var method = typeof(AiController).GetMethod(action)!;
            var route = $"{prefix}/{method.GetCustomAttribute<HttpMethodAttribute>()!.Template}";
            var feature = method.GetCustomAttribute<RequireFeatureAttribute>() is { AiTool: true } f ? f.FeatureKey : null;
            return new BotToolDefinition(route, method.GetCustomAttribute<BotToolAttribute>()!.Capability, feature);
        }

        Assert.Equal(BotTools.SearchKnowledge, Of(nameof(AiController.SearchKnowledge)));
        Assert.Equal(BotTools.ClientInfo, Of(nameof(AiController.GetClientInfo)));
        Assert.Equal(BotTools.Services, Of(nameof(AiController.GetServices)));
        Assert.Equal(BotTools.ContactContext, Of(nameof(AiController.GetContactContext)));
        Assert.Equal(BotTools.UpdateContact, Of(nameof(AiController.UpdateContact)));
        Assert.Equal(BotTools.AvailableSlots, Of(nameof(AiController.GetAvailableSlots)));
        Assert.Equal(BotTools.ScheduleBooking, Of(nameof(AiController.ScheduleBooking)));
        Assert.Equal(BotTools.MyBookings, Of(nameof(AiController.GetMyBookings)));
        Assert.Equal(BotTools.CancelBooking, Of(nameof(AiController.CancelBooking)));
        Assert.Equal(BotTools.Reply, Of(nameof(AiController.Reply)));
        Assert.Equal(BotTools.Handoff, Of(nameof(AiController.HandoffToHuman)));
    }
}
