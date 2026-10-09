using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Controllers.Client;
using PlasticSurgery.Controllers.Integrations;

namespace PlasticSurgery.Tests.Controllers;

public class ChannelIntegrationsControllerTests : ClientControllerTestBase
{
    private readonly Mock<IChannelIntegrationService> _integrations = new();
    private readonly Mock<IMetaGraphClient> _graph = new();
    private readonly Mock<ITelegramIntegrationService> _telegram = new();

    private ChannelIntegrationsController Sut() =>
        new ChannelIntegrationsController(_integrations.Object, _graph.Object, _telegram.Object, Clinic.Object).With();

    private static T Blank<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    [Fact]
    public async Task List_and_refresh_telegram_use_the_signed_in_clinic()
    {
        _integrations.Setup(i => i.ListAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ChannelIntegrationResponse>());
        _telegram.Setup(t => t.RefreshStatusAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<ChannelIntegrationResponse>());
        Assert.IsType<OkObjectResult>((await Sut().List(default)).Result);
        Assert.IsType<OkObjectResult>((await Sut().RefreshTelegram(default)).Result);
    }

    [Fact]
    public async Task Save_overrides_the_clinic_id_in_the_body()
    {
        SaveChannelIntegrationRequest? seen = null;
        _integrations.Setup(i => i.SaveAsync(It.IsAny<SaveChannelIntegrationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SaveChannelIntegrationRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(Blank<ChannelIntegrationResponse>());
        await Sut().Save(Blank<SaveChannelIntegrationRequest>() with { ClinicId = Guid.NewGuid() }, default);
        Assert.Equal(ClinicId, seen!.ClinicId);
    }

    [Fact]
    public async Task Disconnect_is_scoped_to_the_clinic()
    {
        Assert.IsType<NoContentResult>(await Sut().Disconnect("telegram", default));
        _integrations.Verify(i => i.DisconnectAsync(ClinicId, "telegram", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Telegram_connect_succeeds_with_the_clinic_and_token()
    {
        _telegram.Setup(t => t.ConnectAsync(ClinicId, "123:abc", It.IsAny<CancellationToken>())).ReturnsAsync(Blank<ChannelIntegrationResponse>());
        Assert.IsType<OkObjectResult>((await Sut().ConnectTelegram(new ConnectTelegramRequest("123:abc"), default)).Result);
    }

    [Theory]
    [InlineData("argument", 400)]
    [InlineData("telegram", 400)]
    [InlineData("invalid", 409)]
    public async Task Telegram_connect_maps_errors(string kind, int status)
    {
        Exception ex = kind switch
        {
            "argument" => new ArgumentException("bad token"),
            "telegram" => new TelegramApiException("Unauthorized", 401),
            _ => new InvalidOperationException("already connected elsewhere"),
        };
        _telegram.Setup(t => t.ConnectAsync(ClinicId, It.IsAny<string?>(), It.IsAny<CancellationToken>())).ThrowsAsync(ex);
        var result = (await Sut().ConnectTelegram(new ConnectTelegramRequest("x"), default)).Result;
        Assert.Equal(status, ((ObjectResult)result!).StatusCode);
    }

    [Fact]
    public async Task WhatsApp_connect_uses_the_signed_in_clinic_and_maps_provider_failures_to_502()
    {
        ConnectWhatsAppRequest? seen = null;
        _integrations.Setup(i => i.ConnectWhatsAppAsync(It.IsAny<ConnectWhatsAppRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ConnectWhatsAppRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(Blank<ChannelIntegrationResponse>());
        var request = Blank<ConnectWhatsAppRequest>() with { ClinicId = Guid.NewGuid() };
        Assert.IsType<OkObjectResult>((await Sut().ConnectWhatsApp(request, default)).Result);
        Assert.Equal(ClinicId, seen!.ClinicId);

        _integrations.Setup(i => i.ConnectWhatsAppAsync(It.IsAny<ConnectWhatsAppRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MetaGraphApiException("meta says no"));
        var failed = Assert.IsType<ObjectResult>((await Sut().ConnectWhatsApp(request, default)).Result);
        Assert.Equal(StatusCodes.Status502BadGateway, failed.StatusCode);
        Assert.Equal("meta says no", ((ProblemDetails)failed.Value!).Detail);
    }

    [Fact]
    public async Task Facebook_connect_uses_the_signed_in_clinic_and_maps_failures_to_502()
    {
        ConnectFacebookRequest? seen = null;
        _integrations.Setup(i => i.ConnectFacebookAsync(It.IsAny<ConnectFacebookRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ConnectFacebookRequest, CancellationToken>((r, _) => seen = r)
            .ReturnsAsync(Blank<ChannelIntegrationResponse>());
        var request = new ConnectFacebookRequest(Guid.NewGuid(), "code", null);
        Assert.IsType<OkObjectResult>((await Sut().ConnectFacebook(request, default)).Result);
        Assert.Equal(ClinicId, seen!.ClinicId);

        _integrations.Setup(i => i.ConnectFacebookAsync(It.IsAny<ConnectFacebookRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no page"));
        var failed = Assert.IsType<ObjectResult>((await Sut().ConnectFacebook(request, default)).Result);
        Assert.Equal(StatusCodes.Status502BadGateway, failed.StatusCode);
    }

    [Fact]
    public async Task Debug_token_with_an_access_token_does_not_exchange_a_code()
    {
        _graph.Setup(g => g.GetGrantedScopesAsync("tok", It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "whatsapp_business_management" });
        _graph.Setup(g => g.GetClientWhatsAppBusinessAccountsAsync("tok", It.IsAny<CancellationToken>())).ReturnsAsync(new List<ClientWabaInfo>());
        var result = await Sut().DebugWhatsAppToken(new DebugTokenRequest(AccessToken: "tok"), default);
        Assert.IsType<OkObjectResult>(result);
        _graph.Verify(g => g.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Debug_token_exchanges_a_code_when_no_token_is_given()
    {
        _graph.Setup(g => g.ExchangeCodeForTokenAsync("c", "https://r", It.IsAny<CancellationToken>())).ReturnsAsync("exchanged");
        var result = await Sut().DebugWhatsAppToken(new DebugTokenRequest(Code: "c", RedirectUri: "https://r"), default);
        Assert.IsType<OkObjectResult>(result);
        _graph.Verify(g => g.GetGrantedScopesAsync("exchanged", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Debug_token_without_input_or_with_a_provider_failure_is_502()
    {
        var empty = Assert.IsType<ObjectResult>(await Sut().DebugWhatsAppToken(new DebugTokenRequest(), default));
        Assert.Equal(StatusCodes.Status502BadGateway, empty.StatusCode);

        _graph.Setup(g => g.GetGrantedScopesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new MetaGraphApiException("expired"));
        var failed = Assert.IsType<ObjectResult>(await Sut().DebugWhatsAppToken(new DebugTokenRequest(AccessToken: "t"), default));
        Assert.Equal(StatusCodes.Status502BadGateway, failed.StatusCode);
    }
}

public class KnowledgeBenchmarkControllerTests : ClientControllerTestBase
{
    private readonly Mock<IKnowledgeBenchmarkService> _benchmark = new();
    private KnowledgeBenchmarkController Sut() => new KnowledgeBenchmarkController(_benchmark.Object, Clinic.Object).With();

    private static T Blank<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    [Fact]
    public async Task Dashboard_cases_and_runs_are_scoped_to_the_clinic()
    {
        var generation = Guid.NewGuid();
        _benchmark.Setup(b => b.GetDashboardAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<BenchmarkDashboardResponse>());
        _benchmark.Setup(b => b.ListCasesAsync(ClinicId, new BenchmarkCaseFilter("flagged", generation), 5, 10, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<BenchmarkCaseListResponse>());
        _benchmark.Setup(b => b.ListGenerationsAsync(ClinicId, 7, It.IsAny<CancellationToken>())).ReturnsAsync(new List<BenchmarkGenerationSummary>());
        _benchmark.Setup(b => b.ListRunsAsync(ClinicId, 3, It.IsAny<CancellationToken>())).ReturnsAsync(new List<BenchmarkRunSummary>());
        _benchmark.Setup(b => b.ListSourceDocumentsAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<BenchmarkSourceDocumentResponse>());
        var sut = Sut();
        Assert.IsType<OkObjectResult>((await sut.Dashboard(default)).Result);
        Assert.IsType<OkObjectResult>((await sut.ListCases("flagged", generation, 5, 10, default)).Result);
        Assert.IsType<OkObjectResult>((await sut.ListGenerations(7, default)).Result);
        Assert.IsType<OkObjectResult>((await sut.ListRuns(3, default)).Result);
        Assert.IsType<OkObjectResult>((await sut.SourceDocuments(default)).Result);
    }

    [Fact]
    public async Task Missing_records_are_404()
    {
        var sut = Sut();
        var id = Guid.NewGuid();
        Assert.IsType<NotFoundResult>((await sut.GetCase(id, default)).Result);
        Assert.IsType<NotFoundResult>((await sut.GetGeneration(id, default)).Result);
        Assert.IsType<NotFoundResult>((await sut.UpdateCase(id, Blank<UpdateBenchmarkCaseRequest>(), default)).Result);
        Assert.IsType<NotFoundResult>((await sut.SetReviewed(id, Blank<SetBenchmarkCaseReviewedRequest>(), default)).Result);
        Assert.IsType<NotFoundResult>(await sut.DeleteCase(id, default));
        Assert.IsType<NotFoundResult>((await sut.SourceChunks(id, default)).Result);
        Assert.IsType<NotFoundResult>((await sut.GetRun(id, default)).Result);
        Assert.IsType<NotFoundResult>((await sut.ListResults(id, null, 0, 100, default)).Result);
        Assert.IsType<NotFoundResult>((await sut.RunGenerations(id, default)).Result);
        Assert.IsType<NotFoundResult>((await sut.GetResult(id, Guid.NewGuid(), default)).Result);
    }

    [Fact]
    public async Task Found_records_are_returned()
    {
        var id = Guid.NewGuid();
        _benchmark.Setup(b => b.GetCaseAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<BenchmarkCaseDetailResponse>());
        _benchmark.Setup(b => b.GetGenerationAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<BenchmarkGenerationDetail>());
        _benchmark.Setup(b => b.GetRunAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<BenchmarkRunSummary>());
        _benchmark.Setup(b => b.DeleteCaseAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _benchmark.Setup(b => b.ListSourceChunksAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<BenchmarkSourceChunkResponse>());
        _benchmark.Setup(b => b.ListResultsAsync(ClinicId, id, "wrong", 1, 2, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<BenchmarkResultListResponse>());
        _benchmark.Setup(b => b.GetRunGenerationBreakdownAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<BenchmarkRunGenerationBreakdown>());
        var sut = Sut();
        Assert.IsType<OkObjectResult>((await sut.GetCase(id, default)).Result);
        Assert.IsType<OkObjectResult>((await sut.GetGeneration(id, default)).Result);
        Assert.IsType<OkObjectResult>((await sut.GetRun(id, default)).Result);
        Assert.IsType<NoContentResult>(await sut.DeleteCase(id, default));
        Assert.IsType<OkObjectResult>((await sut.SourceChunks(id, default)).Result);
        Assert.IsType<OkObjectResult>((await sut.ListResults(id, "wrong", 1, 2, default)).Result);
        Assert.IsType<OkObjectResult>((await sut.RunGenerations(id, default)).Result);
    }

    [Fact]
    public async Task Generation_is_202_while_pending_and_200_otherwise()
    {
        _benchmark.SetupSequence(b => b.StartGenerationAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartBenchmarkGenerationResponse(Guid.NewGuid(), "pending", 10, 0, 0, [], "started"))
            .ReturnsAsync(new StartBenchmarkGenerationResponse(null, "none", 0, 0, 0, [], "nothing"));
        Assert.IsType<AcceptedResult>((await Sut().GenerateCases(default)).Result);
        Assert.IsType<OkObjectResult>((await Sut().GenerateCases(default)).Result);
    }

    [Fact]
    public async Task Cancelling_a_generation_maps_each_outcome()
    {
        var id = Guid.NewGuid();
        var summary = Blank<BenchmarkGenerationSummary>();
        _benchmark.SetupSequence(b => b.CancelGenerationAsync(ClinicId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerationCancelResult(GenerationCancelStatus.NotFound, null))
            .ReturnsAsync(new GenerationCancelResult(GenerationCancelStatus.NotPending, summary))
            .ReturnsAsync(new GenerationCancelResult(GenerationCancelStatus.Cancelled, summary));
        var sut = Sut();
        Assert.IsType<NotFoundResult>((await sut.CancelGeneration(id, default)).Result);
        Assert.IsType<ConflictObjectResult>((await sut.CancelGeneration(id, default)).Result);
        Assert.Same(summary, Ok(await sut.CancelGeneration(id, default)));
    }

    [Fact]
    public async Task Writes_pass_the_clinic_and_the_user_input()
    {
        var id = Guid.NewGuid();
        var response = Blank<BenchmarkCaseResponse>();
        _benchmark.Setup(b => b.CreateManualCaseAsync(ClinicId, It.IsAny<CreateManualBenchmarkCaseRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(response);
        _benchmark.Setup(b => b.UpdateCaseQuestionAsync(ClinicId, id, "why?", It.IsAny<CancellationToken>())).ReturnsAsync(response);
        _benchmark.Setup(b => b.SetReviewedAsync(ClinicId, id, true, It.IsAny<CancellationToken>())).ReturnsAsync(response);
        var sut = Sut();
        Assert.IsType<OkObjectResult>((await sut.CreateManualCase(Blank<CreateManualBenchmarkCaseRequest>(), default)).Result);
        Assert.IsType<OkObjectResult>((await sut.UpdateCase(id, Blank<UpdateBenchmarkCaseRequest>() with { Question = "why?" }, default)).Result);
        Assert.IsType<OkObjectResult>((await sut.SetReviewed(id, Blank<SetBenchmarkCaseReviewedRequest>() with { Reviewed = true }, default)).Result);
    }

    [Fact]
    public async Task Starting_a_run_is_202_and_forwards_scope_and_generation()
    {
        var generation = Guid.NewGuid();
        _benchmark.Setup(b => b.StartRunAsync(ClinicId, "all", generation, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<BenchmarkRunSummary>()).Verifiable();
        Assert.IsType<AcceptedResult>((await Sut().StartRun(new StartBenchmarkRunRequest("all", generation), default)).Result);
        _benchmark.Verify();
    }

    [Fact]
    public async Task Run_results_detail_comes_back_when_found()
    {
        var run = Guid.NewGuid();
        var result = Guid.NewGuid();
        _benchmark.Setup(b => b.GetResultAsync(ClinicId, run, result, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<BenchmarkResultDetailResponse>());
        Assert.IsType<OkObjectResult>((await Sut().GetResult(run, result, default)).Result);
    }
}

public class WebsiteSourcesControllerTests : ClientControllerTestBase
{
    private readonly Mock<IWebsiteSourceService> _websites = new();
    private WebsiteSourcesController Sut() => new WebsiteSourcesController(_websites.Object, Clinic.Object).With();

    private static T Blank<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    [Fact]
    public async Task List_create_and_set_active_are_scoped_to_the_clinic()
    {
        var id = Guid.NewGuid();
        _websites.Setup(w => w.ListAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WebsiteSourceResponse>());
        _websites.Setup(w => w.CreateAsync(ClinicId, It.IsAny<CreateWebsiteSourceRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(Blank<WebsiteSourceResponse>());
        _websites.Setup(w => w.SetActiveAsync(ClinicId, id, false, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<WebsiteSourceResponse>());
        var sut = Sut();
        Assert.IsType<OkObjectResult>((await sut.List(default)).Result);
        Assert.IsType<AcceptedAtActionResult>((await sut.Create(Blank<CreateWebsiteSourceRequest>(), default)).Result);
        Assert.IsType<OkObjectResult>((await sut.SetActive(id, new SetKnowledgeActiveRequest(false), default)).Result);
    }

    [Fact]
    public async Task Get_pages_set_active_and_rescrape_are_404_for_an_unknown_source()
    {
        var id = Guid.NewGuid();
        var sut = Sut();
        Assert.IsType<NotFoundResult>((await sut.GetById(id, default)).Result);
        Assert.IsType<NotFoundResult>((await sut.Pages(id, null, 0, 200, default)).Result);
        Assert.IsType<NotFoundResult>((await sut.Rescrape(id, default)).Result);
        Assert.IsType<NotFoundResult>((await sut.SetActive(id, new SetKnowledgeActiveRequest(true), default)).Result);
        Assert.IsType<NotFoundResult>(await sut.Delete(id, default));
    }

    [Fact]
    public async Task Pages_are_listed_only_after_the_source_is_confirmed_to_belong_to_the_clinic()
    {
        var id = Guid.NewGuid();
        _websites.Setup(w => w.GetAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<WebsiteSourceDetailResponse>());
        _websites.Setup(w => w.ListPagesAsync(ClinicId, id, "failed", 1, 2, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WebsitePageResponse>());
        var sut = Sut();
        Assert.IsType<OkObjectResult>((await sut.GetById(id, default)).Result);
        Assert.IsType<OkObjectResult>((await sut.Pages(id, "failed", 1, 2, default)).Result);
    }

    [Fact]
    public async Task Rescrape_is_202_and_delete_is_204_when_found()
    {
        var id = Guid.NewGuid();
        _websites.Setup(w => w.RescrapeAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<WebsiteSourceResponse>());
        _websites.Setup(w => w.DeleteAsync(ClinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.IsType<AcceptedResult>((await Sut().Rescrape(id, default)).Result);
        Assert.IsType<NoContentResult>(await Sut().Delete(id, default));
    }

    [Fact]
    public async Task Rescrape_and_delete_turn_argument_errors_into_409()
    {
        var id = Guid.NewGuid();
        _websites.Setup(w => w.RescrapeAsync(ClinicId, id, It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("already running"));
        _websites.Setup(w => w.DeleteAsync(ClinicId, id, It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("in use"));
        Assert.IsType<ConflictObjectResult>((await Sut().Rescrape(id, default)).Result);
        Assert.IsType<ConflictObjectResult>(await Sut().Delete(id, default));
    }
}

public class SmallIntegrationControllerTests
{
    [Fact]
    public void TikTok_reachability_check_answers_ok()
    {
        var sut = new TikTokController(Mock.Of<Microsoft.Extensions.Logging.ILogger<TikTokController>>());
        Assert.IsType<OkObjectResult>(sut.Reachable());
        Assert.IsType<OkResult>(sut.Receive(ControllerTestKit.Json("{\"event\":\"x\"}")));
    }

    [Fact]
    public async Task Calendar_sync_callback_passes_the_payload_to_the_service()
    {
        var service = new Mock<ICalendarIntegrationService>();
        var request = (CalendarSyncCallbackRequest)RuntimeHelpers.GetUninitializedObject(typeof(CalendarSyncCallbackRequest));
        var result = await new CalendarIntegrationsIngestController(service.Object).SyncCallback(request, default);
        Assert.IsType<OkResult>(result);
        service.Verify(s => s.ApplySyncCallbackAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }
}
