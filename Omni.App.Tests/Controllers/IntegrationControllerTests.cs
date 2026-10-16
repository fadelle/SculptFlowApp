using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Controllers.Filters;
using Omni.App.Controllers.Integrations;

namespace Omni.App.Tests.Controllers;

internal static class ControllerTestKit
{
    public static T With<T>(this T controller, Action<HttpContext>? configure = null) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        configure?.Invoke(controller.ControllerContext.HttpContext);
        return controller;
    }

    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    /// <summary>Runs an action filter against a request and reports whether the action ran and which result the filter set.</summary>
    public static async Task<(bool ActionRan, IActionResult? Result)> RunAsync(IAsyncActionFilter filter, IConfiguration configuration, Action<HttpRequest>? request = null)
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(configuration).BuildServiceProvider() };
        request?.Invoke(http.Request);
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        var ran = false;
        await filter.OnActionExecutionAsync(executing, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        });
        return (ran, executing.Result);
    }
}

public class AuthorizationFilterTests
{
    [Fact]
    public async Task Ingest_key_filter_requires_server_config_and_a_matching_header()
    {
        var filter = new RequireIngestKeyAttribute();
        var (ran, result) = await ControllerTestKit.RunAsync(filter, ControllerTestKit.Config());
        Assert.False(ran);
        Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);

        var cfg = ControllerTestKit.Config(("N8n:IngestApiKey", "secret"));
        (ran, result) = await ControllerTestKit.RunAsync(filter, cfg);
        Assert.False(ran);
        Assert.IsType<UnauthorizedObjectResult>(result);
        (ran, result) = await ControllerTestKit.RunAsync(filter, cfg, r => r.Headers[RequireIngestKeyAttribute.HeaderName] = "wrong");
        Assert.False(ran);
        Assert.IsType<UnauthorizedObjectResult>(result);

        (ran, result) = await ControllerTestKit.RunAsync(filter, cfg, r => r.Headers[RequireIngestKeyAttribute.HeaderName] = "secret");
        Assert.True(ran);
        Assert.Null(result);
    }

    [Fact]
    public async Task Platform_admin_filter_hides_the_api_when_unconfigured_and_rejects_bad_keys()
    {
        var filter = new RequirePlatformAdminKeyAttribute();
        var (ran, result) = await ControllerTestKit.RunAsync(filter, ControllerTestKit.Config());
        Assert.False(ran);
        Assert.IsType<NotFoundResult>(result);

        var cfg = ControllerTestKit.Config((RequirePlatformAdminKeyAttribute.ConfigKey, "admin-key"));
        (ran, result) = await ControllerTestKit.RunAsync(filter, cfg);
        Assert.IsType<UnauthorizedObjectResult>(result);
        (ran, result) = await ControllerTestKit.RunAsync(filter, cfg, r => r.Headers[RequirePlatformAdminKeyAttribute.HeaderName] = "admin-kex");
        Assert.False(ran);
        Assert.IsType<UnauthorizedObjectResult>(result);
        (ran, result) = await ControllerTestKit.RunAsync(filter, cfg, r => r.Headers[RequirePlatformAdminKeyAttribute.HeaderName] = "admin-key");
        Assert.True(ran);
        Assert.Null(result);
    }

    [Fact]
    public void Actor_header_is_trimmed_capped_and_defaults()
    {
        var ctx = new DefaultHttpContext();
        Assert.Equal("admin-api", RequirePlatformAdminKeyAttribute.Actor(ctx.Request));
        ctx.Request.Headers[RequirePlatformAdminKeyAttribute.ActorHeaderName] = "  ops@example.com ";
        Assert.Equal("ops@example.com", RequirePlatformAdminKeyAttribute.Actor(ctx.Request));
        ctx.Request.Headers[RequirePlatformAdminKeyAttribute.ActorHeaderName] = new string('a', 500);
        Assert.Equal(200, RequirePlatformAdminKeyAttribute.Actor(ctx.Request).Length);
    }

    [Fact]
    public void A_blank_actor_header_falls_back_to_the_default()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers[RequirePlatformAdminKeyAttribute.ActorHeaderName] = "   ";
        Assert.Equal("admin-api", RequirePlatformAdminKeyAttribute.Actor(ctx.Request));
    }

    [Fact]
    public void Ingest_key_comparison_is_constant_time() =>
        Assert.Contains("FixedTimeEquals", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Omni.App/Controllers/Filters/RequireIngestKeyAttribute.cs")));
}

public class WebhookControllerTests
{
    private readonly Mock<IMetaWebhookProcessor> _processor = new();
    private readonly Mock<IAiTriggerNotifier> _ai = new();

    private WhatsAppWebhookController Sut(string? verifyToken = "verify-me") =>
        new WhatsAppWebhookController(_processor.Object, _ai.Object, ControllerTestKit.Config(("Meta:WebhookVerifyToken", verifyToken)), NullLogger<WhatsAppWebhookController>.Instance, Mock.Of<IMessageTracer>()).With();

    [Fact]
    public void Meta_handshake_echoes_the_challenge_only_for_the_right_token()
    {
        Assert.Equal("12345", Assert.IsType<ContentResult>(Sut().Verify("subscribe", "verify-me", "12345")).Content);
        foreach (var bad in new[] { Sut().Verify("subscribe", "nope", "1"), Sut().Verify("unsubscribe", "verify-me", "1"), Sut().Verify("subscribe", "verify-me", ""), Sut().Verify(null, null, null), Sut(verifyToken: null).Verify("subscribe", "", "1") })
            Assert.Equal(403, Assert.IsType<StatusCodeResult>(bad).StatusCode);
    }

    private const string Secret = "app-secret";

    private WhatsAppWebhookController Signed(string body, string? signature = "valid")
    {
        var sig = signature == "valid"
            ? "sha256=" + Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant()
            : signature;
        return new WhatsAppWebhookController(_processor.Object, _ai.Object, ControllerTestKit.Config(("Meta:AppSecret", Secret)), NullLogger<WhatsAppWebhookController>.Instance, Mock.Of<IMessageTracer>())
            .With(ctx =>
            {
                ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
                if (sig is not null) ctx.Request.Headers["X-Hub-Signature-256"] = sig;
            });
    }

    private void Processed(params WhatsAppWebhookResponse[] responses) =>
        _processor.Setup(p => p.ProcessAllAsync(It.IsAny<JsonElement>(), It.IsAny<CancellationToken>())).ReturnsAsync(responses);

    [Fact]
    public async Task Receive_triggers_the_ai_only_when_the_processor_says_so()
    {
        var client = Guid.NewGuid(); var convo = Guid.NewGuid();
        Processed(new WhatsAppWebhookResponse(true, "customer_message", true, client, ConversationId: convo, ContactId: Guid.NewGuid(), MessageId: Guid.NewGuid(), MessageType: "text", Content: "hi", SelectedValue: "b"));
        Assert.IsType<OkResult>(await Signed("{}").Receive(default));
        _ai.Verify(a => a.NotifyAsync(It.Is<AiTriggerPayload>(p => p.ClientId == client && p.ConversationId == convo && p.Channel == "whatsapp" && p.MessageText == "hi" && p.SelectedValue == "b"), It.IsAny<CancellationToken>()), Times.Once);

        _ai.Invocations.Clear();
        Processed(new WhatsAppWebhookResponse(true, "message_status", false, client));
        await Signed("{}").Receive(default);
        Processed(new WhatsAppWebhookResponse(true, "customer_message", true, client)); // no conversation id
        await Signed("{}").Receive(default);
        _ai.Verify(a => a.NotifyAsync(It.IsAny<AiTriggerPayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_batch_triggers_the_ai_once_per_conversation_using_the_last_message()
    {
        var client = Guid.NewGuid(); var c1 = Guid.NewGuid(); var c2 = Guid.NewGuid();
        WhatsAppWebhookResponse Msg(Guid convo, string text) => new(true, "customer_message", true, client, ConversationId: convo, MessageType: "text", Content: text);
        Processed(Msg(c1, "a"), Msg(c2, "b"), Msg(c1, "c"));
        await Signed("{}").Receive(default);
        _ai.Verify(a => a.NotifyAsync(It.Is<AiTriggerPayload>(p => p.ConversationId == c1 && p.MessageText == "c"), It.IsAny<CancellationToken>()), Times.Once);
        _ai.Verify(a => a.NotifyAsync(It.Is<AiTriggerPayload>(p => p.ConversationId == c2 && p.MessageText == "b"), It.IsAny<CancellationToken>()), Times.Once);
        _ai.Verify(a => a.NotifyAsync(It.IsAny<AiTriggerPayload>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256=deadbeef")]
    [InlineData("")]
    public async Task Forged_posts_without_a_valid_signature_are_rejected(string? signature)
    {
        var result = await Signed("{\"entry\":[]}", signature).Receive(default);
        Assert.IsType<UnauthorizedResult>(result);
        _processor.Verify(p => p.ProcessAllAsync(It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_signature_for_a_different_body_is_rejected()
    {
        var other = ((WhatsAppWebhookController)Signed("{\"a\":1}")).Request.Headers["X-Hub-Signature-256"].ToString();
        var result = await Signed("{\"a\":2}", other).Receive(default);
        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task Without_a_configured_app_secret_nothing_is_accepted()
    {
        var controller = new WhatsAppWebhookController(_processor.Object, _ai.Object, ControllerTestKit.Config(), NullLogger<WhatsAppWebhookController>.Instance, Mock.Of<IMessageTracer>())
            .With(ctx => ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}")));
        Assert.IsType<UnauthorizedResult>(await controller.Receive(default));
        _processor.Verify(p => p.ProcessAllAsync(It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_signed_body_that_is_not_json_is_a_bad_request()
    {
        Assert.IsType<BadRequestResult>(await Signed("not json").Receive(default));
    }
    [Theory]
    [InlineData(WebhookReceiveOutcome.NotFound, 404)]
    [InlineData(WebhookReceiveOutcome.Forbidden, 403)]
    [InlineData(WebhookReceiveOutcome.Accepted, 200)]
    public async Task Telegram_webhook_maps_outcomes_and_passes_the_secret_header(WebhookReceiveOutcome outcome, int status)
    {
        var processor = new Mock<ITelegramWebhookProcessor>();
        var id = Guid.NewGuid();
        processor.Setup(p => p.ReceiveAsync(id, "s3cret", It.IsAny<JsonElement>(), It.IsAny<CancellationToken>())).ReturnsAsync(outcome);
        var controller = new TelegramWebhookController(processor.Object).With(h => h.Request.Headers[TelegramWebhookSecret.HeaderName] = "s3cret");
        Assert.Equal(status, ((IStatusCodeActionResult)await controller.Receive(id, ControllerTestKit.Json("{}"), default)).StatusCode ?? 200);
    }

    [Fact]
    public async Task Ingest_endpoints_return_404_for_unknown_conversations_and_pass_results_through()
    {
        var messages = new Mock<IMessageService>();
        var controller = new MessagesController(messages.Object).With();
        var request = new IngestMessageRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), IngestEventType.CustomerMessage, "whatsapp", "hi", null, null, null, null);
        messages.Setup(m => m.IngestAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(new IngestMessageResult(false, false, null));
        Assert.IsType<NotFoundObjectResult>((await controller.Ingest(request, default)).Result);
        messages.Setup(m => m.IngestAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(new IngestMessageResult(true, false, null, "ai", true));
        Assert.True(((IngestMessageResult)Assert.IsType<OkObjectResult>((await controller.Ingest(request, default)).Result).Value!).AiEligible);

        var templates = new Mock<IWhatsAppTemplateService>();
        var health = new Mock<IWhatsAppHealthService>();
        var events = new WhatsAppIntegrationEventsController(templates.Object, health.Object).With();
        Assert.IsType<OkObjectResult>((await events.TemplateEvent(new WhatsAppTemplateEventRequest(Guid.NewGuid(), "e", null, null, "n", null, null, null, null, null, null, null, null, null), default)).Result);
        Assert.IsType<OkObjectResult>((await events.HealthEvent(new WhatsAppHealthEventRequest(Guid.NewGuid(), "e", null, null, null, null, null, null, null, null), default)).Result);
    }
}

public class AiControllerTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _contactId = Guid.NewGuid();
    private readonly Mock<IClientContext> _clients = new();
    private readonly Mock<ICatalogService> _services = new();
    private readonly Mock<IContactService> _contacts = new();
    private readonly Mock<IBookingService> _bookings = new();
    private readonly Mock<IAvailabilityService> _availability = new();
    private readonly Mock<IConversationService> _conversations = new();
    private readonly Mock<IKnowledgeSearchService> _search = new();

    private AiController Sut() => new AiController(_clients.Object, _services.Object, _contacts.Object, _bookings.Object, _availability.Object, _conversations.Object, _search.Object).With();

    private static UpcomingBookingResponse Upcoming(Guid? id = null) => new(id ?? Guid.NewGuid(), "booked", "standard", null, null, DateTimeOffset.UtcNow, null, "2026-01-01", "10:00", "Thursday, Jan 1 at 10:00 AM");

    [Fact]
    public async Task Knowledge_search_validates_input_and_reports_provider_outages_as_503()
    {
        Assert.IsType<BadRequestObjectResult>((await Sut().SearchKnowledge(new KnowledgeSearchRequest(Guid.Empty, "q"), default)).Result);
        Assert.IsType<BadRequestObjectResult>((await Sut().SearchKnowledge(new KnowledgeSearchRequest(_clientId, " "), default)).Result);
        _search.Setup(s => s.SearchAsync(_clientId, "price", 3, It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeSearchResponse([]));
        Assert.IsType<OkObjectResult>((await Sut().SearchKnowledge(new KnowledgeSearchRequest(_clientId, "price", 3), default)).Result);
        _search.Setup(s => s.SearchAsync(_clientId, "down", null, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("embeddings down"));
        Assert.Equal(503, Assert.IsType<ObjectResult>((await Sut().SearchKnowledge(new KnowledgeSearchRequest(_clientId, "down"), default)).Result).StatusCode);
    }

    [Fact]
    public async Task Client_info_services_and_contact_lookups()
    {
        Assert.IsType<NotFoundResult>((await Sut().GetClientInfo(_clientId, default)).Result);
        _clients.Setup(c => c.GetByIdAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client { Id = _clientId, Name = "Glow", Timezone = "Asia/Beirut" });
        var info = (ClientInfoResponse)Assert.IsType<OkObjectResult>((await Sut().GetClientInfo(_clientId, default)).Result).Value!;
        Assert.Equal(("Glow", "Asia/Beirut"), (info.Name, info.Timezone));

        await Sut().GetServices(_clientId, default);
        _services.Verify(p => p.ListAsync(_clientId, true, It.IsAny<CancellationToken>()), Times.Once);

        Assert.IsType<NotFoundResult>((await Sut().GetContactContext(_contactId, _clientId, default)).Result);
        Assert.IsType<NotFoundResult>((await Sut().UpdateContact(_contactId, _clientId, new UpdateContactContextRequest(null, null, null, null, null, null, null), default)).Result);
    }

    [Fact]
    public async Task Available_slots_defaults_the_range_and_merges_the_contacts_booking_context()
    {
        var slots = new AvailabilityResponse(true, "UTC", "2026-01-01", 30, "2026-01-01", "2026-01-07", null, []);
        _availability.Setup(a => a.GetSlotsAsync(_clientId, null, null, 7, It.IsAny<CancellationToken>())).ReturnsAsync(slots);
        Assert.Same(slots, Assert.IsType<OkObjectResult>((await Sut().GetAvailableSlots(_clientId, null, null, null, null, default)).Result).Value);

        var date = new DateOnly(2026, 2, 3);
        _availability.Setup(a => a.GetSlotsAsync(_clientId, null, date, 1, It.IsAny<CancellationToken>())).ReturnsAsync(slots);
        _bookings.Setup(a => a.GetBookingContextAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(new ContactBookingContext(true, false, "existing_upcoming_booking", [Upcoming()]));
        var merged = (AvailabilityResponse)Assert.IsType<OkObjectResult>((await Sut().GetAvailableSlots(_clientId, _contactId, null, date, null, default)).Result).Value!;
        Assert.Equal(("2026-02-03", true, false, "existing_upcoming_booking"), (merged.RequestedDate, merged.HasUpcomingBooking, merged.CanCreateNewBooking, merged.BookingBlockReason));

        _bookings.Setup(a => a.GetBookingContextAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync((ContactBookingContext?)null);
        Assert.IsType<NotFoundObjectResult>((await Sut().GetAvailableSlots(_clientId, _contactId, null, null, null, default)).Result);
    }

    [Theory]
    [InlineData("BOOKED", true)]
    [InlineData("RESCHEDULED", true)]
    [InlineData("SLOT_UNAVAILABLE", false)]
    [InlineData("CONFIRMATION_REQUIRED", false)]
    public async Task Schedule_reports_success_only_for_real_changes_and_hides_context_on_success(string code, bool success)
    {
        var existing = new[] { Upcoming() };
        _bookings.Setup(a => a.ScheduleAsync(_clientId, _contactId, It.IsAny<ScheduleBookingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScheduleOutcome(success ? "created" : "none", code, "msg", Existing: existing, RequestedLabel: "Friday at 10"));
        var r = (ScheduleBookingResult)Assert.IsType<OkObjectResult>((await Sut().ScheduleBooking(_clientId, _contactId, new ScheduleBookingRequest(DateTimeOffset.UtcNow), default)).Result).Value!;
        Assert.Equal(success, r.Success);
        Assert.Equal(success, r.ExistingUpcomingBookings is null);
    }

    [Fact]
    public async Task Book_maps_every_domain_failure_to_an_instruction_for_the_ai_and_never_says_booked()
    {
        var request = new BookSlotRequest(_contactId, null, null, DateTimeOffset.UtcNow.AddDays(2), null, null, null, null);
        BookSlotResult Result(Task<ActionResult<BookSlotResult>> t) => (BookSlotResult)Assert.IsType<OkObjectResult>(t.Result.Result).Value!;

        var appt = new BookingResponse(Guid.NewGuid(), _clientId, _contactId, "Ann", null, null, "standard", "booked", DateTimeOffset.UtcNow, null, null, null, null, DateTimeOffset.UtcNow);
        _bookings.Setup(a => a.BookAvailableSlotAsync(It.IsAny<CreateBookingRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(appt);
        var local = Upcoming(appt.Id);
        _bookings.Setup(a => a.GetUpcomingForContactAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(new UpcomingBookingsResponse("UTC", [local]));
        var ok = Result(Sut().BookSlot(_clientId, request, default));
        Assert.Equal((true, "BOOKED", local), (ok.Success, ok.Code, ok.Booking));

        _bookings.Setup(a => a.BookAvailableSlotAsync(It.IsAny<CreateBookingRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ContactAlreadyBookedException(Upcoming()));
        var dup = Result(Sut().BookSlot(_clientId, request, default));
        Assert.Equal(("EXISTING_UPCOMING_BOOKING", false), (dup.Code, dup.Success));
        Assert.StartsWith("Nothing was booked", dup.Instruction);

        _bookings.Setup(a => a.BookAvailableSlotAsync(It.IsAny<CreateBookingRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new SlotUnavailableException("taken"));
        Assert.Equal("SLOT_UNAVAILABLE", Result(Sut().BookSlot(_clientId, request, default)).Code);
        _bookings.Setup(a => a.BookAvailableSlotAsync(It.IsAny<CreateBookingRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("Contact not found for this client."));
        Assert.Equal("CONTACT_NOT_FOUND", Result(Sut().BookSlot(_clientId, request, default)).Code);
        _bookings.Setup(a => a.BookAvailableSlotAsync(It.IsAny<CreateBookingRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("Service inactive"));
        Assert.Equal("INVALID_REQUEST", Result(Sut().BookSlot(_clientId, request, default)).Code);
    }

    [Fact]
    public async Task Reschedule_returns_404_for_nothing_to_move_and_409_with_hints_for_conflicts()
    {
        var request = new RescheduleBookingRequest(DateTimeOffset.UtcNow.AddDays(3), null, "r");
        Assert.IsType<NotFoundObjectResult>((await Sut().RescheduleBooking(_clientId, _contactId, null, request, default)).Result);

        _bookings.Setup(a => a.RescheduleAsync(_clientId, _contactId, null, request.ScheduledStart, null, "r", It.IsAny<CancellationToken>())).ThrowsAsync(new MultipleUpcomingBookingsException([Upcoming(), Upcoming()]));
        Assert.Equal(409, Assert.IsType<ConflictObjectResult>((await Sut().RescheduleBooking(_clientId, _contactId, null, request, default)).Result).StatusCode);
        _bookings.Setup(a => a.RescheduleAsync(_clientId, _contactId, null, request.ScheduledStart, null, "r", It.IsAny<CancellationToken>())).ThrowsAsync(new SlotUnavailableException("busy"));
        Assert.IsType<ConflictObjectResult>((await Sut().RescheduleBooking(_clientId, _contactId, null, request, default)).Result);

        var appt = new BookingResponse(Guid.NewGuid(), _clientId, _contactId, null, null, null, "standard", "booked", DateTimeOffset.UtcNow, null, null, null, null, DateTimeOffset.UtcNow);
        _bookings.Setup(a => a.RescheduleAsync(_clientId, _contactId, appt.Id, request.ScheduledStart, null, "r", It.IsAny<CancellationToken>())).ReturnsAsync(appt);
        Assert.IsType<OkObjectResult>((await Sut().RescheduleBooking(_clientId, _contactId, appt.Id, request, default)).Result);
    }

    [Fact]
    public async Task Cancel_upcoming_and_handoff()
    {
        _bookings.Setup(a => a.CancelBookingAsync(_clientId, _contactId, null, "r", It.IsAny<CancellationToken>())).ReturnsAsync(new CancelOutcome("canceled", "CANCELED", "ok", Upcoming()));
        var r = (CancelBookingResult)Assert.IsType<OkObjectResult>((await Sut().CancelBooking(_clientId, _contactId, null, new CancelBookingRequest("r"), default)).Result).Value!;
        Assert.True(r.Success);
        _bookings.Setup(a => a.GetUpcomingForContactAsync(_clientId, _contactId, It.IsAny<CancellationToken>())).ReturnsAsync(new UpcomingBookingsResponse("UTC", []));
        Assert.IsType<OkObjectResult>((await Sut().GetMyBookings(_clientId, _contactId, default)).Result);

        var convo = Guid.NewGuid();
        Assert.IsType<NotFoundResult>((await Sut().HandoffToHuman(convo, _clientId, new HandoffToHumanRequest("why"), default)).Result);
        _conversations.Setup(c => c.HandoffToHumanAsync(_clientId, convo, "why", It.IsAny<CancellationToken>())).ReturnsAsync(new ConversationResponse(convo, _clientId, _contactId, "whatsapp", "active", "human", false, true, null, null, null, false, DateTimeOffset.UtcNow));
        Assert.IsType<OkObjectResult>((await Sut().HandoffToHuman(convo, _clientId, new HandoffToHumanRequest("why"), default)).Result);
    }
}
