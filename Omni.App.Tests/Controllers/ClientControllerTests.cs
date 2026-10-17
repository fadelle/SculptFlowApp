using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Controllers.Client;

namespace Omni.App.Tests.Controllers;

/// <summary>Shared setup: a signed-in client user (or none) resolved through ICurrentClientContext.</summary>
public abstract class ClientControllerTestBase
{
    protected readonly Guid ClientId = Guid.NewGuid();
    protected readonly Mock<ICurrentClientContext> Client = new();

    protected ClientControllerTestBase() => SignedIn();

    protected void SignedIn() => Client.Setup(c => c.GetClientIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ClientId);

    protected void NoClient() => Client.Setup(c => c.GetClientIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

    protected static T Ok<T>(ActionResult<T> result) => (T)Assert.IsType<OkObjectResult>(result.Result).Value!;

    protected static void IsForbid(ActionResult result) => Assert.IsType<ForbidResult>(result);

    protected static void IsForbid<T>(ActionResult<T> result) => Assert.IsType<ForbidResult>(result.Result);
}

public class ContactsAndBookingsControllerTests : ClientControllerTestBase
{
    private readonly Mock<IContactService> _contacts = new();
    private readonly Mock<IBookingService> _bookings = new();
    private readonly Mock<IAvailabilityService> _availability = new();

    private ContactsController Contacts() => new ContactsController(_contacts.Object, Client.Object).With();
    private BookingsController Appts() => new BookingsController(_bookings.Object, _availability.Object, Client.Object).With();

    private ContactResponse Contact() => new(Guid.NewGuid(), ClientId, null, null, "Ann", null, null, null, null, "web", null, null, null, "new", "unknown", null, null, null, null, true, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Contact_creation_is_201_for_new_and_200_for_existing()
    {
        var req = new CreateContactRequest(ClientId, null, "Ann", null, null, null, null, "web", null, null, "ext", null, null, null, null);
        _contacts.Setup(l => l.CreateOrGetAsync(req, It.IsAny<CancellationToken>())).ReturnsAsync((Contact(), true));
        Assert.IsType<CreatedAtActionResult>((await Contacts().Create(req, default)).Result);
        _contacts.Setup(l => l.CreateOrGetAsync(req, It.IsAny<CancellationToken>())).ReturnsAsync((Contact(), false));
        Assert.IsType<OkObjectResult>((await Contacts().Create(req, default)).Result);
    }

    [Fact]
    public void Contact_intake_needs_the_ingest_key_because_it_takes_the_client_from_the_body()
    {
        var create = typeof(ContactsController).GetMethod(nameof(ContactsController.Create))!;
        Assert.NotEmpty(create.GetCustomAttributes(typeof(Omni.App.Controllers.Filters.RequireIngestKeyAttribute), inherit: true));
    }

    [Fact]
    public async Task Contact_reads_are_scoped_to_the_signed_in_client_and_paging_is_clamped()
    {
        _contacts.Setup(l => l.ListAsync(ClientId, "new", null, "q", 5, 200, It.IsAny<CancellationToken>())).ReturnsAsync((new List<ContactResponse>() as IReadOnlyList<ContactResponse>, 0));
        Assert.IsType<OkObjectResult>((await Contacts().List("new", null, "q", 5, 9999, default)).Result);
        _contacts.Setup(l => l.ListAsync(ClientId, null, null, null, 0, 1, It.IsAny<CancellationToken>())).ReturnsAsync((new List<ContactResponse>() as IReadOnlyList<ContactResponse>, 0));
        Assert.IsType<OkObjectResult>((await Contacts().List(null, null, null, 0, -4, default)).Result);

        Assert.IsType<NotFoundResult>((await Contacts().GetById(Guid.NewGuid(), default)).Result);
        var contact = Contact();
        _contacts.Setup(l => l.GetByIdAsync(ClientId, contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        Assert.Same(contact, Ok(await Contacts().GetById(contact.Id, default)));
    }

    [Fact]
    public async Task Contact_writes_return_404_when_missing_and_everything_is_forbidden_without_a_client()
    {
        Assert.IsType<NotFoundResult>((await Contacts().Update(Guid.NewGuid(), new UpdateContactRequest(null, null, null, null, null, null, null, null, null, null, null), default)).Result);
        Assert.IsType<NotFoundResult>((await Contacts().UpdateStage(Guid.NewGuid(), new UpdateContactStageRequest("new", null), default)).Result);

        NoClient();
        Assert.IsType<ForbidResult>((await Contacts().List(null, null, null, 0, 10, default)).Result);
        Assert.IsType<ForbidResult>((await Contacts().GetById(Guid.NewGuid(), default)).Result);
        Assert.IsType<ForbidResult>((await Contacts().Update(Guid.NewGuid(), new UpdateContactRequest(null, null, null, null, null, null, null, null, null, null, null), default)).Result);
        Assert.IsType<ForbidResult>((await Contacts().UpdateStage(Guid.NewGuid(), new UpdateContactStageRequest("new", null), default)).Result);
    }

    [Fact]
    public async Task Booking_creation_always_uses_the_signed_in_client_not_the_one_in_the_body()
    {
        var other = Guid.NewGuid();
        var req = new CreateBookingRequest(other, Guid.NewGuid(), null, "standard", DateTimeOffset.UtcNow.AddDays(1), null, null, null, null);
        CreateBookingRequest? seen = null;
        _bookings.Setup(a => a.CreateAsync(It.IsAny<CreateBookingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateBookingRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(new BookingResponse(Guid.NewGuid(), ClientId, req.ContactId, null, null, null, "standard", "booked", req.ScheduledStart, null, null, null, null, DateTimeOffset.UtcNow));
        Assert.IsType<CreatedAtActionResult>((await Appts().Create(req, default)).Result);
        Assert.Equal(ClientId, seen!.ClientId);
    }

    [Fact]
    public async Task Booking_reads_status_and_availability()
    {
        _availability.Setup(a => a.GetSlotsAsync(ClientId, null, null, 7, It.IsAny<CancellationToken>())).ReturnsAsync(new AvailabilityResponse(true, "UTC", "t", 30, "a", "b", null, []));
        Assert.IsType<OkObjectResult>((await Appts().GetAvailable(null, null, null, default)).Result);
        var date = new DateOnly(2026, 3, 1);
        _availability.Setup(a => a.GetSlotsAsync(ClientId, null, date, 1, It.IsAny<CancellationToken>())).ReturnsAsync(new AvailabilityResponse(true, "UTC", "t", 30, "a", "b", null, []));
        Assert.IsType<OkObjectResult>((await Appts().GetAvailable(null, date, null, default)).Result);

        Assert.IsType<NotFoundResult>((await Appts().GetById(Guid.NewGuid(), default)).Result);
        Assert.IsType<NotFoundResult>((await Appts().UpdateStatus(Guid.NewGuid(), new UpdateBookingStatusRequest("attended"), default)).Result);
        _bookings.Setup(a => a.ListAsync(ClientId, "booked", null, null, 0, 200, It.IsAny<CancellationToken>())).ReturnsAsync((new List<BookingResponse>() as IReadOnlyList<BookingResponse>, 0));
        Assert.IsType<OkObjectResult>((await Appts().List("booked", null, null, 0, 5000, default)).Result);
        NoClient();
        Assert.IsType<ForbidResult>((await Appts().GetAvailable(null, null, null, default)).Result);
        Assert.IsType<ForbidResult>((await Appts().List(null, null, null, 0, 10, default)).Result);
    }

    [Theory]
    [InlineData(0, 2026)]
    [InlineData(13, 2026)]
    [InlineData(5, 1899)]
    [InlineData(5, 3001)]
    public async Task Calendar_rejects_out_of_range_months_and_years(int month, int year)
    {
        Assert.IsType<BadRequestObjectResult>((await Appts().GetCalendarMonth(year, month, null, default)).Result);
        _bookings.Verify(a => a.GetCalendarMonthAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TimeZoneInfo?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Calendar_uses_the_viewers_cookie_zone_only_for_the_mine_view()
    {
        _bookings.Setup(a => a.GetCalendarMonthAsync(ClientId, 2026, 3, null, It.IsAny<CancellationToken>())).ReturnsAsync(new CalendarMonthResponse(2026, 3, "UTC", "UTC", "a", "b", [], 0));
        Assert.IsType<OkObjectResult>((await Appts().GetCalendarMonth(2026, 3, null, default)).Result);

        var controller = new BookingsController(_bookings.Object, _availability.Object, Client.Object).With(h => h.Request.Headers.Cookie = $"{ViewerTimeZone.CookieName}=Asia/Beirut");
        _bookings.Setup(a => a.GetCalendarMonthAsync(ClientId, 2026, 3, It.Is<TimeZoneInfo?>(z => z != null && z.Id == "Asia/Beirut"), It.IsAny<CancellationToken>())).ReturnsAsync(new CalendarMonthResponse(2026, 3, "Asia/Beirut", "UTC", "a", "b", [], 0));
        Assert.IsType<OkObjectResult>((await controller.GetCalendarMonth(2026, 3, "mine", default)).Result);
        Assert.IsType<OkObjectResult>((await controller.GetCalendarMonth(2026, 3, "client", default)).Result);
    }
}

public class ConversationsControllerTests : ClientControllerTestBase
{
    private readonly Mock<IConversationService> _conversations = new();
    private readonly Mock<IMessageService> _messages = new();
    private string? _ingestKey = "ingest-secret";

    private ConversationsController Sut(Action<HttpContext>? configure = null) =>
        new ConversationsController(_conversations.Object, _messages.Object, ControllerTestKit.Config(("N8n:IngestApiKey", _ingestKey)), Client.Object).With(configure);

    private static MessageResponse Msg() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "outbound", "member", "dashboard", "whatsapp", "text", "hi", null, false, null, null, null, null, DateTimeOffset.UtcNow, null, null, null, null, null, null, null);

    private ConversationResponse Convo() => new(Guid.NewGuid(), ClientId, Guid.NewGuid(), "whatsapp", "active", "ai", true, false, null, null, null, false, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Create_list_and_read_use_the_signed_in_client()
    {
        var convo = Convo();
        CreateConversationRequest? seen = null;
        _conversations.Setup(c => c.CreateAsync(It.IsAny<CreateConversationRequest>(), It.IsAny<CancellationToken>())).Callback<CreateConversationRequest, CancellationToken>((r, _) => seen = r).ReturnsAsync(convo);
        Assert.IsType<CreatedAtActionResult>((await Sut().Create(new CreateConversationRequest(Guid.NewGuid(), Guid.NewGuid(), "whatsapp", null), default)).Result);
        Assert.Equal(ClientId, seen!.ClientId);

        _conversations.Setup(c => c.ListAsync(ClientId, 0, 200, It.IsAny<CancellationToken>())).ReturnsAsync((new List<ConversationListRow>() as IReadOnlyList<ConversationListRow>, 0));
        Assert.IsType<OkObjectResult>((await Sut().List(0, 999, default)).Result);
        Assert.IsType<NotFoundResult>((await Sut().GetById(convo.Id, default)).Result);
        _conversations.Setup(c => c.GetByIdWithMessagesAsync(ClientId, convo.Id, It.IsAny<CancellationToken>())).ReturnsAsync((convo, new List<MessageResponse>() as IReadOnlyList<MessageResponse>));
        Assert.IsType<OkObjectResult>((await Sut().GetById(convo.Id, default)).Result);
        _conversations.Setup(c => c.GetMessagesAsync(ClientId, convo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<MessageResponse>());
        Assert.IsType<OkObjectResult>((await Sut().GetMessages(convo.Id, default)).Result);
    }

    [Fact]
    public async Task Mode_close_read_and_template_actions_return_404_for_foreign_conversations()
    {
        var id = Guid.NewGuid();
        Assert.IsType<NotFoundResult>((await Sut().TakeOver(id, default)).Result);
        Assert.IsType<NotFoundResult>((await Sut().ReturnToAi(id, default)).Result);
        Assert.IsType<NotFoundResult>((await Sut().Close(id, default)).Result);
        Assert.IsType<NotFoundResult>((await Sut().Reopen(id, default)).Result);
        Assert.IsType<NotFoundResult>((await Sut().AddMessage(id, new CreateMessageRequest("inbound", "contact", "whatsapp", null, "x", null), default)).Result);
        Assert.IsType<NotFoundResult>((await Sut().SendTemplateMessage(id, new SendTemplateMessageRequest(Guid.NewGuid(), null), default)).Result);
        Assert.IsType<NotFoundResult>(await Sut().MarkRead(id, default));
        _conversations.Setup(c => c.MarkReadAsync(ClientId, id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.IsType<NoContentResult>(await Sut().MarkRead(id, default));

        var convo = Convo();
        _conversations.Setup(c => c.TakeOverAsync(ClientId, id, It.IsAny<CancellationToken>())).ReturnsAsync(convo);
        _conversations.Setup(c => c.ReturnToAiAsync(ClientId, id, It.IsAny<CancellationToken>())).ReturnsAsync(convo);
        _conversations.Setup(c => c.CloseAsync(ClientId, id, It.IsAny<CancellationToken>())).ReturnsAsync(convo);
        _conversations.Setup(c => c.ReopenAsync(ClientId, id, It.IsAny<CancellationToken>())).ReturnsAsync(convo);
        Assert.IsType<OkObjectResult>((await Sut().TakeOver(id, default)).Result);
        Assert.IsType<OkObjectResult>((await Sut().ReturnToAi(id, default)).Result);
        Assert.IsType<OkObjectResult>((await Sut().Close(id, default)).Result);
        Assert.IsType<OkObjectResult>((await Sut().Reopen(id, default)).Result);
    }

    [Fact]
    public async Task Every_member_action_is_forbidden_without_a_client()
    {
        NoClient();
        var id = Guid.NewGuid();
        IsForbid(await Sut().Create(new CreateConversationRequest(Guid.Empty, Guid.Empty, "whatsapp", null), default));
        IsForbid(await Sut().List(0, 10, default));
        IsForbid(await Sut().GetById(id, default));
        IsForbid(await Sut().GetMessages(id, default));
        IsForbid(await Sut().TakeOver(id, default));
        IsForbid(await Sut().ReturnToAi(id, default));
        IsForbid(await Sut().Close(id, default));
        IsForbid(await Sut().Reopen(id, default));
        Assert.IsType<ForbidResult>(await Sut().MarkRead(id, default));
    }

    [Fact]
    public async Task A_member_send_needs_a_signed_in_user_with_a_client()
    {
        var id = Guid.NewGuid();
        var request = new SendMessageRequest("hello");
        Assert.IsType<ChallengeResult>((await Sut().SendMessage(id, null, request, default)).Result); // anonymous, not the AI

        var signedIn = Sut(h => h.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", "u1")], "test")));
        _messages.Setup(m => m.SendAsync(ClientId, id, request, It.IsAny<CancellationToken>())).ReturnsAsync(Msg());
        Assert.IsType<OkObjectResult>((await signedIn.SendMessage(id, Guid.NewGuid(), request, default)).Result); // client id in the query is ignored
        NoClient();
        IsForbid(await signedIn.SendMessage(id, null, request, default));
        SignedIn();
        _messages.Setup(m => m.SendAsync(ClientId, id, request, It.IsAny<CancellationToken>())).ReturnsAsync((MessageResponse?)null);
        Assert.IsType<NotFoundResult>((await signedIn.SendMessage(id, null, request, default)).Result);
    }

    [Fact]
    public async Task An_ai_send_requires_the_ingest_key_and_a_client_id_and_maps_human_mode_to_409()
    {
        var id = Guid.NewGuid(); var client = Guid.NewGuid();
        var request = new SendMessageRequest("hi", Sender: "AI");

        Assert.IsType<UnauthorizedObjectResult>((await Sut().SendMessage(id, client, request, default)).Result);
        Assert.IsType<UnauthorizedObjectResult>((await Sut(h => h.Request.Headers[RequireIngestKeyAttributeName] = "wrong").SendMessage(id, client, request, default)).Result);
        _ingestKey = null;
        Assert.IsType<UnauthorizedObjectResult>((await Sut(h => h.Request.Headers[RequireIngestKeyAttributeName] = "ingest-secret").SendMessage(id, client, request, default)).Result);
        _ingestKey = "ingest-secret";

        SendAs ai = () => Sut(h => h.Request.Headers[RequireIngestKeyAttributeName] = "ingest-secret");
        Assert.IsType<BadRequestObjectResult>((await ai().SendMessage(id, null, request, default)).Result);

        _messages.Setup(m => m.SendAiReplyAsync(client, id, "hi", It.IsAny<CancellationToken>())).ReturnsAsync(Msg());
        Assert.IsType<OkObjectResult>((await ai().SendMessage(id, client, request, default)).Result);
        _messages.Setup(m => m.SendAiReplyAsync(client, id, "hi", It.IsAny<CancellationToken>())).ThrowsAsync(new ConversationNotInAiModeException("human"));
        var conflict = Assert.IsType<ConflictObjectResult>((await ai().SendMessage(id, client, request, default)).Result);
        Assert.Contains("conversation_in_human_mode", System.Text.Json.JsonSerializer.Serialize(conflict.Value));
        _messages.Setup(m => m.SendAiReplyAsync(client, id, "hi", It.IsAny<CancellationToken>())).ReturnsAsync((MessageResponse?)null);
        Assert.IsType<NotFoundResult>((await ai().SendMessage(id, client, request, default)).Result);
    }

    private delegate ConversationsController SendAs();

    private const string RequireIngestKeyAttributeName = "X-Ingest-Key";
}

public class CampaignKnowledgeAndServiceControllerTests : ClientControllerTestBase
{
    private readonly Mock<ICampaignService> _campaigns = new();
    private readonly Mock<ICampaignAudienceService> _audience = new();
    private readonly Mock<IKnowledgeService> _knowledge = new();
    private readonly Mock<IKnowledgeSettingsService> _settings = new();
    private readonly Mock<ICatalogService> _services = new();
    private readonly Mock<IOrderService> _orders = new();

    private CampaignsController Campaigns() => new CampaignsController(_campaigns.Object, _audience.Object, Client.Object).With();
    private KnowledgeController Knowledge() => new KnowledgeController(_knowledge.Object, _settings.Object, Client.Object).With();

    [Fact]
    public async Task Audience_preview_validates_the_type_and_caps_the_sample()
    {
        Assert.IsType<BadRequestObjectResult>((await Campaigns().AudiencePreview("bogus", null, default)).Result);
        _audience.Setup(a => a.GetMatchingCountAsync(ClientId, CampaignAudienceType.AllEligible, "{}", It.IsAny<CancellationToken>())).ReturnsAsync(42);
        Assert.Equal(42, Ok(await Campaigns().AudiencePreview(CampaignAudienceType.AllEligible, "{}", default)).MatchingContacts);

        Assert.IsType<BadRequestObjectResult>((await Campaigns().AudiencePreviewContacts("bogus", null, 5, default)).Result);
        var contacts = Enumerable.Range(0, 80).Select(i => new Contact { Id = Guid.NewGuid(), FullName = $"L{i}" }).ToList();
        _audience.Setup(a => a.GetEligibleContactsAsync(ClientId, CampaignAudienceType.AllEligible, null, It.IsAny<CancellationToken>())).ReturnsAsync(contacts);
        Assert.Equal((80, 10), (Ok(await Campaigns().AudiencePreviewContacts(CampaignAudienceType.AllEligible, null, 0, default)) is { } a ? (a.MatchingContacts, a.Contacts.Count) : default));
        Assert.Equal(50, Ok(await Campaigns().AudiencePreviewContacts(CampaignAudienceType.AllEligible, null, 50, default)).Contacts.Count);
        Assert.Equal(10, Ok(await Campaigns().AudiencePreviewContacts(CampaignAudienceType.AllEligible, null, 51, default)).Contacts.Count);
    }

    [Fact]
    public async Task Campaign_actions_return_404_for_unknown_campaigns_and_use_the_signed_in_client()
    {
        var id = Guid.NewGuid();
        Assert.IsType<NotFoundResult>((await Campaigns().GetById(id, default)).Result);
        Assert.IsType<NotFoundResult>((await Campaigns().Schedule(id, new ScheduleCampaignRequest(DateTimeOffset.UtcNow), default)).Result);
        Assert.IsType<NotFoundResult>((await Campaigns().Send(id, 20, default)).Result);
        Assert.IsType<NotFoundResult>((await Campaigns().ProcessBatch(id, 20, default)).Result);
        Assert.IsType<NotFoundResult>((await Campaigns().Cancel(id, default)).Result);
        Assert.IsType<NotFoundResult>((await Campaigns().Stop(id, default)).Result);
        var stopped = new CampaignResponse(id, ClientId, "Spring", CampaignType.Custom, CampaignChannel.WhatsApp, null, null,
            CampaignAudienceType.Custom, null, CampaignStatus.Paused, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        _campaigns.Setup(c => c.StopAsync(ClientId, id, It.IsAny<CancellationToken>())).ReturnsAsync(stopped);
        Assert.Same(stopped, Assert.IsType<OkObjectResult>((await Campaigns().Stop(id, default)).Result).Value);

        CreateCampaignRequest? seen = null;
        _campaigns.Setup(c => c.CreateAsync(It.IsAny<CreateCampaignRequest>(), It.IsAny<CancellationToken>())).Callback<CreateCampaignRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(new CampaignResponse(id, ClientId, "n", "custom", "whatsapp", Guid.NewGuid(), "t", "custom", null, "draft", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        Assert.IsType<CreatedAtActionResult>((await Campaigns().Create(new CreateCampaignRequest(Guid.NewGuid(), "n", Guid.NewGuid(), [], null, null), default)).Result);
        Assert.Equal(ClientId, seen!.ClientId);

        _campaigns.Setup(c => c.ListAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignListRow>());
        Assert.IsType<OkObjectResult>((await Campaigns().List(default)).Result);
        NoClient();
        IsForbid(await Campaigns().List(default));
        IsForbid(await Campaigns().Cancel(id, default));
        IsForbid(await Campaigns().Stop(id, default));
    }

    [Fact]
    public async Task Knowledge_503s_when_the_embedding_service_is_unavailable()
    {
        _knowledge.Setup(k => k.CreateAsync(ClientId, It.IsAny<SaveKnowledgeRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("embeddings down"));
        Assert.Equal(503, Assert.IsType<ObjectResult>((await Knowledge().Create(new SaveKnowledgeRequest("t", "faq", "c"), default)).Result).StatusCode);
        var id = Guid.NewGuid();
        _knowledge.Setup(k => k.UpdateAsync(ClientId, id, It.IsAny<SaveKnowledgeRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("embeddings down"));
        Assert.Equal(503, Assert.IsType<ObjectResult>((await Knowledge().Update(id, new SaveKnowledgeRequest("t", "faq", "c"), default)).Result).StatusCode);
    }

    [Fact]
    public async Task Knowledge_crud_upload_and_settings()
    {
        var id = Guid.NewGuid();
        Assert.IsType<NotFoundResult>((await Knowledge().GetById(id, default)).Result);
        Assert.IsType<NotFoundResult>((await Knowledge().Update(id, new SaveKnowledgeRequest("t", "faq", "c"), default)).Result);
        Assert.IsType<NotFoundResult>((await Knowledge().SetActive(id, new SetKnowledgeActiveRequest(false), default)).Result);
        Assert.IsType<NotFoundResult>(await Knowledge().Delete(id, default));
        _knowledge.Setup(k => k.DeleteAsync(ClientId, id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.IsType<NoContentResult>(await Knowledge().Delete(id, default));

        var doc = new KnowledgeDocumentResponse(id, ClientId, "t", "faq", "c", true, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "manual", null, null, null, null);
        _knowledge.Setup(k => k.CreateAsync(ClientId, It.IsAny<SaveKnowledgeRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        Assert.IsType<CreatedAtActionResult>((await Knowledge().Create(new SaveKnowledgeRequest("t", "faq", "c"), default)).Result);
        _knowledge.Setup(k => k.ListAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeDocumentResponse> { doc });
        Assert.Single(Ok(await Knowledge().List(default)));

        Assert.IsType<BadRequestObjectResult>((await Knowledge().Upload(new KnowledgeUploadForm(), default)).Result);
        var file = new FormFile(new MemoryStream(Encoding.UTF8.GetBytes("hello")), 0, 5, "File", "faq.txt");
        _knowledge.Setup(k => k.CreateFromUploadAsync(ClientId, It.Is<UploadKnowledgeRequest>(u => u.Category == "general"), "faq.txt", It.IsAny<Stream>(), 5, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        Assert.IsType<CreatedAtActionResult>((await Knowledge().Upload(new KnowledgeUploadForm { File = file, Category = " " }, default)).Result);

        _settings.Setup(s => s.GetAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeSettingsResponse("m", 8, "cosine", "none", 200, 20, 5, 0.3, DateTimeOffset.UtcNow));
        Assert.IsType<OkObjectResult>((await Knowledge().GetSettings(default)).Result);
        _settings.Setup(s => s.UpdateAsync(ClientId, It.IsAny<UpdateKnowledgeSettingsRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeSettingsResponse("m", 8, "cosine", "none", 200, 20, 6, 0.3, DateTimeOffset.UtcNow));
        Assert.Equal(6, Ok(await Knowledge().UpdateSettings(new UpdateKnowledgeSettingsRequest(200, 20, 6, 0.3), default)).TopK);

        NoClient();
        IsForbid(await Knowledge().List(default));
        IsForbid(await Knowledge().GetSettings(default));
        Assert.IsType<ForbidResult>(await Knowledge().Delete(id, default));
    }

    [Fact]
    public async Task Services_and_orders_scope_to_the_client()
    {
        var services = new ServicesController(_services.Object, Client.Object).With();
        var id = Guid.NewGuid();
        Assert.IsType<NotFoundResult>((await services.GetById(id, default)).Result);
        Assert.IsType<NotFoundResult>((await services.Update(id, new UpdateServiceRequest("n", null, null, null), default)).Result);
        Assert.IsType<NotFoundResult>((await services.SetActive(id, new SetServiceActiveRequest(false), default)).Result);
        CreateServiceRequest? seen = null;
        _services.Setup(p => p.CreateAsync(It.IsAny<CreateServiceRequest>(), It.IsAny<CancellationToken>())).Callback<CreateServiceRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(new ServiceResponse(id, ClientId, "n", null, null, null, true));
        Assert.IsType<CreatedAtActionResult>((await services.Create(new CreateServiceRequest(Guid.NewGuid(), "n", null, null, null), default)).Result);
        Assert.Equal(ClientId, seen!.ClientId);
        _services.Setup(p => p.ListAsync(ClientId, false, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ServiceResponse>());
        Assert.IsType<OkObjectResult>((await services.List(false, default)).Result);

        var ordersController = new OrdersController(_orders.Object, Client.Object).With();
        Assert.IsType<NotFoundResult>((await ordersController.Update(id, new UpdateOrderRequest(null, null, null, null, null, null, null), default)).Result);
        _orders.Setup(b => b.ListAsync(ClientId, "quoted", 0, 200, It.IsAny<CancellationToken>())).ReturnsAsync((new List<OrderResponse>() as IReadOnlyList<OrderResponse>, 0));
        Assert.IsType<OkObjectResult>((await ordersController.List("quoted", 0, 9999, default)).Result);
        NoClient();
        IsForbid(await services.List(true, default));
        IsForbid(await ordersController.List(null, 0, 10, default));
    }
}
