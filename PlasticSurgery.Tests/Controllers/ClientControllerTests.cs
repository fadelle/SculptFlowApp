using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Controllers.Client;

namespace PlasticSurgery.Tests.Controllers;

/// <summary>Shared setup: a signed-in clinic user (or none) resolved through ICurrentClinicContext.</summary>
public abstract class ClientControllerTestBase
{
    protected readonly Guid ClinicId = Guid.NewGuid();
    protected readonly Mock<ICurrentClinicContext> Clinic = new();

    protected ClientControllerTestBase() => SignedIn();

    protected void SignedIn() => Clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ClinicId);

    protected void NoClinic() => Clinic.Setup(c => c.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

    protected static T Ok<T>(ActionResult<T> result) => (T)Assert.IsType<OkObjectResult>(result.Result).Value!;

    protected static void IsForbid(ActionResult result) => Assert.IsType<ForbidResult>(result);

    protected static void IsForbid<T>(ActionResult<T> result) => Assert.IsType<ForbidResult>(result.Result);
}

public class LeadsAndAppointmentsControllerTests : ClientControllerTestBase
{
    private readonly Mock<ILeadService> _leads = new();
    private readonly Mock<IAppointmentService> _appointments = new();
    private readonly Mock<IAvailabilityService> _availability = new();

    private LeadsController Leads() => new LeadsController(_leads.Object, Clinic.Object).With();
    private AppointmentsController Appts() => new AppointmentsController(_appointments.Object, _availability.Object, Clinic.Object).With();

    private LeadResponse Lead() => new(Guid.NewGuid(), ClinicId, null, null, "Ann", null, null, null, null, "web", null, null, null, "new", "unknown", null, null, null, null, true, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Lead_creation_is_201_for_new_and_200_for_existing()
    {
        var req = new CreateLeadRequest(ClinicId, null, "Ann", null, null, null, null, "web", null, null, "ext", null, null, null, null);
        _leads.Setup(l => l.CreateOrGetAsync(req, It.IsAny<CancellationToken>())).ReturnsAsync((Lead(), true));
        Assert.IsType<CreatedAtActionResult>((await Leads().Create(req, default)).Result);
        _leads.Setup(l => l.CreateOrGetAsync(req, It.IsAny<CancellationToken>())).ReturnsAsync((Lead(), false));
        Assert.IsType<OkObjectResult>((await Leads().Create(req, default)).Result);
    }

    [Fact]
    public async Task Lead_reads_are_scoped_to_the_signed_in_clinic_and_paging_is_clamped()
    {
        _leads.Setup(l => l.ListAsync(ClinicId, "new", null, "q", 5, 200, It.IsAny<CancellationToken>())).ReturnsAsync((new List<LeadResponse>() as IReadOnlyList<LeadResponse>, 0));
        Assert.IsType<OkObjectResult>((await Leads().List("new", null, "q", 5, 9999, default)).Result);
        _leads.Setup(l => l.ListAsync(ClinicId, null, null, null, 0, 1, It.IsAny<CancellationToken>())).ReturnsAsync((new List<LeadResponse>() as IReadOnlyList<LeadResponse>, 0));
        Assert.IsType<OkObjectResult>((await Leads().List(null, null, null, 0, -4, default)).Result);

        Assert.IsType<NotFoundResult>((await Leads().GetById(Guid.NewGuid(), default)).Result);
        var lead = Lead();
        _leads.Setup(l => l.GetByIdAsync(ClinicId, lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        Assert.Same(lead, Ok(await Leads().GetById(lead.Id, default)));
    }

    [Fact]
    public async Task Lead_writes_return_404_when_missing_and_everything_is_forbidden_without_a_clinic()
    {
        Assert.IsType<NotFoundResult>((await Leads().Update(Guid.NewGuid(), new UpdateLeadRequest(null, null, null, null, null, null, null, null, null, null, null), default)).Result);
        Assert.IsType<NotFoundResult>((await Leads().UpdateStatus(Guid.NewGuid(), new UpdateLeadStatusRequest("new", null), default)).Result);

        NoClinic();
        Assert.IsType<ForbidResult>((await Leads().List(null, null, null, 0, 10, default)).Result);
        Assert.IsType<ForbidResult>((await Leads().GetById(Guid.NewGuid(), default)).Result);
        Assert.IsType<ForbidResult>((await Leads().Update(Guid.NewGuid(), new UpdateLeadRequest(null, null, null, null, null, null, null, null, null, null, null), default)).Result);
        Assert.IsType<ForbidResult>((await Leads().UpdateStatus(Guid.NewGuid(), new UpdateLeadStatusRequest("new", null), default)).Result);
    }

    [Fact]
    public async Task Appointment_creation_always_uses_the_signed_in_clinic_not_the_one_in_the_body()
    {
        var other = Guid.NewGuid();
        var req = new CreateAppointmentRequest(other, Guid.NewGuid(), null, "consultation", DateTimeOffset.UtcNow.AddDays(1), null, null, null, null);
        CreateAppointmentRequest? seen = null;
        _appointments.Setup(a => a.CreateAsync(It.IsAny<CreateAppointmentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateAppointmentRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(new AppointmentResponse(Guid.NewGuid(), ClinicId, req.LeadId, null, null, null, "consultation", "booked", req.ScheduledStart, null, null, null, null, DateTimeOffset.UtcNow));
        Assert.IsType<CreatedAtActionResult>((await Appts().Create(req, default)).Result);
        Assert.Equal(ClinicId, seen!.ClinicId);
    }

    [Fact]
    public async Task Appointment_reads_status_and_availability()
    {
        _availability.Setup(a => a.GetSlotsAsync(ClinicId, null, null, 7, It.IsAny<CancellationToken>())).ReturnsAsync(new AvailabilityResponse(true, "UTC", "t", 30, "a", "b", null, []));
        Assert.IsType<OkObjectResult>((await Appts().GetAvailable(null, null, null, default)).Result);
        var date = new DateOnly(2026, 3, 1);
        _availability.Setup(a => a.GetSlotsAsync(ClinicId, null, date, 1, It.IsAny<CancellationToken>())).ReturnsAsync(new AvailabilityResponse(true, "UTC", "t", 30, "a", "b", null, []));
        Assert.IsType<OkObjectResult>((await Appts().GetAvailable(null, date, null, default)).Result);

        Assert.IsType<NotFoundResult>((await Appts().GetById(Guid.NewGuid(), default)).Result);
        Assert.IsType<NotFoundResult>((await Appts().UpdateStatus(Guid.NewGuid(), new UpdateAppointmentStatusRequest("attended"), default)).Result);
        _appointments.Setup(a => a.ListAsync(ClinicId, "booked", null, null, 0, 200, It.IsAny<CancellationToken>())).ReturnsAsync((new List<AppointmentResponse>() as IReadOnlyList<AppointmentResponse>, 0));
        Assert.IsType<OkObjectResult>((await Appts().List("booked", null, null, 0, 5000, default)).Result);
        NoClinic();
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
        _appointments.Verify(a => a.GetCalendarMonthAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TimeZoneInfo?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Calendar_uses_the_viewers_cookie_zone_only_for_the_mine_view()
    {
        _appointments.Setup(a => a.GetCalendarMonthAsync(ClinicId, 2026, 3, null, It.IsAny<CancellationToken>())).ReturnsAsync(new CalendarMonthResponse(2026, 3, "UTC", "UTC", "a", "b", [], 0));
        Assert.IsType<OkObjectResult>((await Appts().GetCalendarMonth(2026, 3, null, default)).Result);

        var controller = new AppointmentsController(_appointments.Object, _availability.Object, Clinic.Object).With(h => h.Request.Headers.Cookie = $"{ViewerTimeZone.CookieName}=Asia/Beirut");
        _appointments.Setup(a => a.GetCalendarMonthAsync(ClinicId, 2026, 3, It.Is<TimeZoneInfo?>(z => z != null && z.Id == "Asia/Beirut"), It.IsAny<CancellationToken>())).ReturnsAsync(new CalendarMonthResponse(2026, 3, "Asia/Beirut", "UTC", "a", "b", [], 0));
        Assert.IsType<OkObjectResult>((await controller.GetCalendarMonth(2026, 3, "mine", default)).Result);
        Assert.IsType<OkObjectResult>((await controller.GetCalendarMonth(2026, 3, "clinic", default)).Result);
    }
}

public class ConversationsControllerTests : ClientControllerTestBase
{
    private readonly Mock<IConversationService> _conversations = new();
    private readonly Mock<IMessageService> _messages = new();
    private string? _ingestKey = "ingest-secret";

    private ConversationsController Sut(Action<HttpContext>? configure = null) =>
        new ConversationsController(_conversations.Object, _messages.Object, ControllerTestKit.Config(("N8n:IngestApiKey", _ingestKey)), Clinic.Object).With(configure);

    private static MessageResponse Msg() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "outbound", "staff", "dashboard", "whatsapp", "text", "hi", null, false, null, null, null, null, DateTimeOffset.UtcNow, null, null, null, null, null, null, null);

    private ConversationResponse Convo() => new(Guid.NewGuid(), ClinicId, Guid.NewGuid(), "whatsapp", "active", "ai", true, false, null, null, null, false, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Create_list_and_read_use_the_signed_in_clinic()
    {
        var convo = Convo();
        CreateConversationRequest? seen = null;
        _conversations.Setup(c => c.CreateAsync(It.IsAny<CreateConversationRequest>(), It.IsAny<CancellationToken>())).Callback<CreateConversationRequest, CancellationToken>((r, _) => seen = r).ReturnsAsync(convo);
        Assert.IsType<CreatedAtActionResult>((await Sut().Create(new CreateConversationRequest(Guid.NewGuid(), Guid.NewGuid(), "whatsapp", null), default)).Result);
        Assert.Equal(ClinicId, seen!.ClinicId);

        _conversations.Setup(c => c.ListAsync(ClinicId, 0, 200, It.IsAny<CancellationToken>())).ReturnsAsync((new List<ConversationListRow>() as IReadOnlyList<ConversationListRow>, 0));
        Assert.IsType<OkObjectResult>((await Sut().List(0, 999, default)).Result);
        Assert.IsType<NotFoundResult>((await Sut().GetById(convo.Id, default)).Result);
        _conversations.Setup(c => c.GetByIdWithMessagesAsync(ClinicId, convo.Id, It.IsAny<CancellationToken>())).ReturnsAsync((convo, new List<MessageResponse>() as IReadOnlyList<MessageResponse>));
        Assert.IsType<OkObjectResult>((await Sut().GetById(convo.Id, default)).Result);
        _conversations.Setup(c => c.GetMessagesAsync(ClinicId, convo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<MessageResponse>());
        Assert.IsType<OkObjectResult>((await Sut().GetMessages(convo.Id, default)).Result);
    }

    [Fact]
    public async Task Mode_close_read_and_template_actions_return_404_for_foreign_conversations()
    {
        var id = Guid.NewGuid();
        Assert.IsType<NotFoundResult>((await Sut().TakeOver(id, default)).Result);
        Assert.IsType<NotFoundResult>((await Sut().ReturnToAi(id, default)).Result);
        Assert.IsType<NotFoundResult>((await Sut().Close(id, default)).Result);
        Assert.IsType<NotFoundResult>((await Sut().AddMessage(id, new CreateMessageRequest("inbound", "lead", "whatsapp", null, "x", null), default)).Result);
        Assert.IsType<NotFoundResult>((await Sut().SendTemplateMessage(id, new SendTemplateMessageRequest(Guid.NewGuid(), null), default)).Result);
        Assert.IsType<NotFoundResult>(await Sut().MarkRead(id, default));
        _conversations.Setup(c => c.MarkReadAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.IsType<NoContentResult>(await Sut().MarkRead(id, default));

        var convo = Convo();
        _conversations.Setup(c => c.TakeOverAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(convo);
        _conversations.Setup(c => c.ReturnToAiAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(convo);
        _conversations.Setup(c => c.CloseAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(convo);
        Assert.IsType<OkObjectResult>((await Sut().TakeOver(id, default)).Result);
        Assert.IsType<OkObjectResult>((await Sut().ReturnToAi(id, default)).Result);
        Assert.IsType<OkObjectResult>((await Sut().Close(id, default)).Result);
    }

    [Fact]
    public async Task Every_staff_action_is_forbidden_without_a_clinic()
    {
        NoClinic();
        var id = Guid.NewGuid();
        IsForbid(await Sut().Create(new CreateConversationRequest(Guid.Empty, Guid.Empty, "whatsapp", null), default));
        IsForbid(await Sut().List(0, 10, default));
        IsForbid(await Sut().GetById(id, default));
        IsForbid(await Sut().GetMessages(id, default));
        IsForbid(await Sut().TakeOver(id, default));
        IsForbid(await Sut().ReturnToAi(id, default));
        IsForbid(await Sut().Close(id, default));
        Assert.IsType<ForbidResult>(await Sut().MarkRead(id, default));
    }

    [Fact]
    public async Task A_staff_send_needs_a_signed_in_user_with_a_clinic()
    {
        var id = Guid.NewGuid();
        var request = new SendMessageRequest("hello");
        Assert.IsType<ChallengeResult>((await Sut().SendMessage(id, null, request, default)).Result); // anonymous, not the AI

        var signedIn = Sut(h => h.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", "u1")], "test")));
        _messages.Setup(m => m.SendAsync(ClinicId, id, request, It.IsAny<CancellationToken>())).ReturnsAsync(Msg());
        Assert.IsType<OkObjectResult>((await signedIn.SendMessage(id, Guid.NewGuid(), request, default)).Result); // clinic id in the query is ignored
        NoClinic();
        IsForbid(await signedIn.SendMessage(id, null, request, default));
        SignedIn();
        _messages.Setup(m => m.SendAsync(ClinicId, id, request, It.IsAny<CancellationToken>())).ReturnsAsync((MessageResponse?)null);
        Assert.IsType<NotFoundResult>((await signedIn.SendMessage(id, null, request, default)).Result);
    }

    [Fact]
    public async Task An_ai_send_requires_the_ingest_key_and_a_clinic_id_and_maps_human_mode_to_409()
    {
        var id = Guid.NewGuid(); var clinic = Guid.NewGuid();
        var request = new SendMessageRequest("hi", Sender: "AI");

        Assert.IsType<UnauthorizedObjectResult>((await Sut().SendMessage(id, clinic, request, default)).Result);
        Assert.IsType<UnauthorizedObjectResult>((await Sut(h => h.Request.Headers[RequireIngestKeyAttributeName] = "wrong").SendMessage(id, clinic, request, default)).Result);
        _ingestKey = null;
        Assert.IsType<UnauthorizedObjectResult>((await Sut(h => h.Request.Headers[RequireIngestKeyAttributeName] = "ingest-secret").SendMessage(id, clinic, request, default)).Result);
        _ingestKey = "ingest-secret";

        SendAs ai = () => Sut(h => h.Request.Headers[RequireIngestKeyAttributeName] = "ingest-secret");
        Assert.IsType<BadRequestObjectResult>((await ai().SendMessage(id, null, request, default)).Result);

        _messages.Setup(m => m.SendAiReplyAsync(clinic, id, "hi", It.IsAny<CancellationToken>())).ReturnsAsync(Msg());
        Assert.IsType<OkObjectResult>((await ai().SendMessage(id, clinic, request, default)).Result);
        _messages.Setup(m => m.SendAiReplyAsync(clinic, id, "hi", It.IsAny<CancellationToken>())).ThrowsAsync(new ConversationNotInAiModeException("human"));
        var conflict = Assert.IsType<ConflictObjectResult>((await ai().SendMessage(id, clinic, request, default)).Result);
        Assert.Contains("conversation_in_human_mode", System.Text.Json.JsonSerializer.Serialize(conflict.Value));
        _messages.Setup(m => m.SendAiReplyAsync(clinic, id, "hi", It.IsAny<CancellationToken>())).ReturnsAsync((MessageResponse?)null);
        Assert.IsType<NotFoundResult>((await ai().SendMessage(id, clinic, request, default)).Result);
    }

    private delegate ConversationsController SendAs();

    private const string RequireIngestKeyAttributeName = "X-Ingest-Key";
}

public class CampaignKnowledgeAndProcedureControllerTests : ClientControllerTestBase
{
    private readonly Mock<ICampaignService> _campaigns = new();
    private readonly Mock<ICampaignAudienceService> _audience = new();
    private readonly Mock<IKnowledgeService> _knowledge = new();
    private readonly Mock<IKnowledgeSettingsService> _settings = new();
    private readonly Mock<IProcedureService> _procedures = new();
    private readonly Mock<IProcedureBookingService> _bookings = new();

    private CampaignsController Campaigns() => new CampaignsController(_campaigns.Object, _audience.Object, Clinic.Object).With();
    private KnowledgeController Knowledge() => new KnowledgeController(_knowledge.Object, _settings.Object, Clinic.Object).With();

    [Fact]
    public async Task Audience_preview_validates_the_type_and_caps_the_sample()
    {
        Assert.IsType<BadRequestObjectResult>((await Campaigns().AudiencePreview("bogus", null, default)).Result);
        _audience.Setup(a => a.GetMatchingCountAsync(ClinicId, CampaignAudienceType.AllEligible, "{}", It.IsAny<CancellationToken>())).ReturnsAsync(42);
        Assert.Equal(42, Ok(await Campaigns().AudiencePreview(CampaignAudienceType.AllEligible, "{}", default)).MatchingLeads);

        Assert.IsType<BadRequestObjectResult>((await Campaigns().AudiencePreviewLeads("bogus", null, 5, default)).Result);
        var leads = Enumerable.Range(0, 80).Select(i => new Lead { Id = Guid.NewGuid(), FullName = $"L{i}" }).ToList();
        _audience.Setup(a => a.GetEligibleLeadsAsync(ClinicId, CampaignAudienceType.AllEligible, null, It.IsAny<CancellationToken>())).ReturnsAsync(leads);
        Assert.Equal((80, 10), (Ok(await Campaigns().AudiencePreviewLeads(CampaignAudienceType.AllEligible, null, 0, default)) is { } a ? (a.MatchingLeads, a.Leads.Count) : default));
        Assert.Equal(50, Ok(await Campaigns().AudiencePreviewLeads(CampaignAudienceType.AllEligible, null, 50, default)).Leads.Count);
        Assert.Equal(10, Ok(await Campaigns().AudiencePreviewLeads(CampaignAudienceType.AllEligible, null, 51, default)).Leads.Count);
    }

    [Fact]
    public async Task Campaign_actions_return_404_for_unknown_campaigns_and_use_the_signed_in_clinic()
    {
        var id = Guid.NewGuid();
        Assert.IsType<NotFoundResult>((await Campaigns().GetById(id, default)).Result);
        Assert.IsType<NotFoundResult>((await Campaigns().Schedule(id, new ScheduleCampaignRequest(DateTimeOffset.UtcNow), default)).Result);
        Assert.IsType<NotFoundResult>((await Campaigns().Send(id, 20, default)).Result);
        Assert.IsType<NotFoundResult>((await Campaigns().ProcessBatch(id, 20, default)).Result);
        Assert.IsType<NotFoundResult>((await Campaigns().Cancel(id, default)).Result);

        CreateCampaignRequest? seen = null;
        _campaigns.Setup(c => c.CreateAsync(It.IsAny<CreateCampaignRequest>(), It.IsAny<CancellationToken>())).Callback<CreateCampaignRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(new CampaignResponse(id, ClinicId, "n", "custom", "whatsapp", Guid.NewGuid(), "t", "custom", null, "draft", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        Assert.IsType<CreatedAtActionResult>((await Campaigns().Create(new CreateCampaignRequest(Guid.NewGuid(), "n", Guid.NewGuid(), [], null, null), default)).Result);
        Assert.Equal(ClinicId, seen!.ClinicId);

        _campaigns.Setup(c => c.ListAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignListRow>());
        Assert.IsType<OkObjectResult>((await Campaigns().List(default)).Result);
        NoClinic();
        IsForbid(await Campaigns().List(default));
        IsForbid(await Campaigns().Cancel(id, default));
    }

    [Fact]
    public async Task Knowledge_503s_when_the_embedding_service_is_unavailable()
    {
        _knowledge.Setup(k => k.CreateAsync(ClinicId, It.IsAny<SaveKnowledgeRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("embeddings down"));
        Assert.Equal(503, Assert.IsType<ObjectResult>((await Knowledge().Create(new SaveKnowledgeRequest("t", "faq", "c"), default)).Result).StatusCode);
        var id = Guid.NewGuid();
        _knowledge.Setup(k => k.UpdateAsync(ClinicId, id, It.IsAny<SaveKnowledgeRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("embeddings down"));
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
        _knowledge.Setup(k => k.DeleteAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.IsType<NoContentResult>(await Knowledge().Delete(id, default));

        var doc = new KnowledgeDocumentResponse(id, ClinicId, "t", "faq", "c", true, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "manual", null, null, null, null);
        _knowledge.Setup(k => k.CreateAsync(ClinicId, It.IsAny<SaveKnowledgeRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        Assert.IsType<CreatedAtActionResult>((await Knowledge().Create(new SaveKnowledgeRequest("t", "faq", "c"), default)).Result);
        _knowledge.Setup(k => k.ListAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeDocumentResponse> { doc });
        Assert.Single(Ok(await Knowledge().List(default)));

        Assert.IsType<BadRequestObjectResult>((await Knowledge().Upload(new KnowledgeUploadForm(), default)).Result);
        var file = new FormFile(new MemoryStream(Encoding.UTF8.GetBytes("hello")), 0, 5, "File", "faq.txt");
        _knowledge.Setup(k => k.CreateFromUploadAsync(ClinicId, It.Is<UploadKnowledgeRequest>(u => u.Category == "general"), "faq.txt", It.IsAny<Stream>(), 5, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        Assert.IsType<CreatedAtActionResult>((await Knowledge().Upload(new KnowledgeUploadForm { File = file, Category = " " }, default)).Result);

        _settings.Setup(s => s.GetAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeSettingsResponse("m", 8, "cosine", "none", 200, 20, 5, 0.3, DateTimeOffset.UtcNow));
        Assert.IsType<OkObjectResult>((await Knowledge().GetSettings(default)).Result);
        _settings.Setup(s => s.UpdateAsync(ClinicId, It.IsAny<UpdateKnowledgeSettingsRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeSettingsResponse("m", 8, "cosine", "none", 200, 20, 6, 0.3, DateTimeOffset.UtcNow));
        Assert.Equal(6, Ok(await Knowledge().UpdateSettings(new UpdateKnowledgeSettingsRequest(200, 20, 6, 0.3), default)).TopK);

        NoClinic();
        IsForbid(await Knowledge().List(default));
        IsForbid(await Knowledge().GetSettings(default));
        Assert.IsType<ForbidResult>(await Knowledge().Delete(id, default));
    }

    [Fact]
    public async Task Procedures_and_bookings_scope_to_the_clinic()
    {
        var procedures = new ProceduresController(_procedures.Object, Clinic.Object).With();
        var id = Guid.NewGuid();
        Assert.IsType<NotFoundResult>((await procedures.GetById(id, default)).Result);
        Assert.IsType<NotFoundResult>((await procedures.Update(id, new UpdateProcedureRequest("n", null, null, null), default)).Result);
        Assert.IsType<NotFoundResult>((await procedures.SetActive(id, new SetProcedureActiveRequest(false), default)).Result);
        CreateProcedureRequest? seen = null;
        _procedures.Setup(p => p.CreateAsync(It.IsAny<CreateProcedureRequest>(), It.IsAny<CancellationToken>())).Callback<CreateProcedureRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(new ProcedureResponse(id, ClinicId, "n", null, null, null, true));
        Assert.IsType<CreatedAtActionResult>((await procedures.Create(new CreateProcedureRequest(Guid.NewGuid(), "n", null, null, null), default)).Result);
        Assert.Equal(ClinicId, seen!.ClinicId);
        _procedures.Setup(p => p.ListAsync(ClinicId, false, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ProcedureResponse>());
        Assert.IsType<OkObjectResult>((await procedures.List(false, default)).Result);

        var bookingsController = new ProcedureBookingsController(_bookings.Object, Clinic.Object).With();
        Assert.IsType<NotFoundResult>((await bookingsController.Update(id, new UpdateProcedureBookingRequest(null, null, null, null, null, null, null), default)).Result);
        _bookings.Setup(b => b.ListAsync(ClinicId, "quoted", 0, 200, It.IsAny<CancellationToken>())).ReturnsAsync((new List<ProcedureBookingResponse>() as IReadOnlyList<ProcedureBookingResponse>, 0));
        Assert.IsType<OkObjectResult>((await bookingsController.List("quoted", 0, 9999, default)).Result);
        NoClinic();
        IsForbid(await procedures.List(true, default));
        IsForbid(await bookingsController.List(null, 0, 10, default));
    }
}
