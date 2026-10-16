using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Controllers.Admin;

namespace Omni.App.Tests.Controllers;

/// <summary>The platform-admin benchmark API: the client comes from the route, answers pass straight through, and the
/// status codes the portal's page relies on (202 for queued work, 204 for delete) are kept.</summary>
public class KnowledgeBenchmarkAdminControllerTests
{
    private readonly Mock<IKnowledgeBenchmarkAdminService> _benchmark = new();
    private readonly Guid _clientId = Guid.NewGuid();

    private KnowledgeBenchmarkAdminController Sut() => new KnowledgeBenchmarkAdminController(_benchmark.Object).With();

    private static T Blank<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    [Fact]
    public async Task Reads_pass_the_route_client_and_query_through()
    {
        var generation = Guid.NewGuid();
        var id = Guid.NewGuid();
        _benchmark.Setup(b => b.GetDashboardAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<BenchmarkDashboardResponse>());
        _benchmark.Setup(b => b.ListCasesAsync(_clientId, new BenchmarkCaseFilter("all", generation), 0, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Blank<BenchmarkCaseListResponse>()).Verifiable();
        _benchmark.Setup(b => b.ListResultsAsync(_clientId, id, "MISS", 5, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Blank<BenchmarkResultListResponse>()).Verifiable();
        var sut = Sut();
        Assert.IsType<OkObjectResult>(await sut.Dashboard(_clientId, default));
        Assert.IsType<OkObjectResult>(await sut.ListCases(_clientId, "all", generation, -3, 10, default)); // a negative skip reads from 0
        Assert.IsType<OkObjectResult>(await sut.ListResults(_clientId, id, "MISS", 5, 20, default));
        Assert.IsType<OkObjectResult>(await sut.ListGenerations(_clientId, 7, default));
        Assert.IsType<OkObjectResult>(await sut.ListRuns(_clientId, 3, default));
        Assert.IsType<OkObjectResult>(await sut.SourceDocuments(_clientId, default));
        Assert.IsType<OkObjectResult>(await sut.SourceChunks(_clientId, id, default));
        Assert.IsType<OkObjectResult>(await sut.GetCase(_clientId, id, default));
        Assert.IsType<OkObjectResult>(await sut.GetGeneration(_clientId, id, default));
        Assert.IsType<OkObjectResult>(await sut.GetRun(_clientId, id, default));
        Assert.IsType<OkObjectResult>(await sut.RunGenerations(_clientId, id, default));
        Assert.IsType<OkObjectResult>(await sut.GetResult(_clientId, id, Guid.NewGuid(), default));
        _benchmark.Verify();
    }

    [Fact]
    public async Task Generation_is_202_while_pending_and_200_otherwise()
    {
        _benchmark.SetupSequence(b => b.StartGenerationAsync(_clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartBenchmarkGenerationResponse(Guid.NewGuid(), "pending", 10, 0, 0, [], "started"))
            .ReturnsAsync(new StartBenchmarkGenerationResponse(null, "none", 0, 0, 0, [], "nothing"));
        Assert.IsType<AcceptedResult>(await Sut().GenerateCases(_clientId, default));
        Assert.IsType<OkObjectResult>(await Sut().GenerateCases(_clientId, default));
    }

    [Fact]
    public async Task Writes_pass_the_client_and_the_input()
    {
        var id = Guid.NewGuid();
        var generation = Guid.NewGuid();
        var request = new StartBenchmarkRunRequest("generation", generation);
        _benchmark.Setup(b => b.UpdateCaseQuestionAsync(_clientId, id, "why?", It.IsAny<CancellationToken>())).ReturnsAsync(Blank<BenchmarkCaseResponse>()).Verifiable();
        _benchmark.Setup(b => b.SetReviewedAsync(_clientId, id, true, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<BenchmarkCaseResponse>()).Verifiable();
        _benchmark.Setup(b => b.StartRunAsync(_clientId, request, It.IsAny<CancellationToken>())).ReturnsAsync(Blank<BenchmarkRunSummary>()).Verifiable();
        var sut = Sut();
        Assert.IsType<OkObjectResult>(await sut.CreateManualCase(_clientId, new CreateManualBenchmarkCaseRequest("q", id, id), default));
        Assert.IsType<OkObjectResult>(await sut.UpdateCase(_clientId, id, new UpdateBenchmarkCaseRequest("why?"), default));
        Assert.IsType<OkObjectResult>(await sut.SetReviewed(_clientId, id, new SetBenchmarkCaseReviewedRequest(true), default));
        Assert.IsType<OkObjectResult>(await sut.CancelGeneration(_clientId, id, default));
        Assert.IsType<AcceptedResult>(await sut.StartRun(_clientId, request, default));
        Assert.IsType<NoContentResult>(await sut.DeleteCase(_clientId, id, default));
        _benchmark.Verify();
        _benchmark.Verify(b => b.DeleteCaseAsync(_clientId, id, It.IsAny<CancellationToken>()), Times.Once);
    }
}
