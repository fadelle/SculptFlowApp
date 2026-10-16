using System.Runtime.CompilerServices;
using Omni.App.Business.Services.PlatformAdmin;

namespace Omni.App.Tests.Services;

/// <summary>The admin portal's benchmark entry point: checks the client, delegates with that client id, and turns
/// "not this client's" answers into KeyNotFoundException (404).</summary>
public class KnowledgeBenchmarkAdminServiceTests
{
    private readonly Mock<IKnowledgeBenchmarkService> _benchmark = new();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Guid _clientId = Guid.NewGuid();

    public KnowledgeBenchmarkAdminServiceTests()
    {
        _clients.Setup(c => c.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client { Id = _clientId });
    }

    private KnowledgeBenchmarkAdminService Sut() => new(_benchmark.Object, _clients.Object);

    private static T Blank<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    [Fact]
    public async Task An_unknown_client_is_404_and_the_benchmark_is_never_called()
    {
        var other = Guid.NewGuid();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().GetDashboardAsync(other, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().StartGenerationAsync(other, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().StartRunAsync(other, new StartBenchmarkRunRequest("all"), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().CreateManualCaseAsync(other, Blank<CreateManualBenchmarkCaseRequest>(), default));
        _benchmark.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reads_and_writes_go_to_the_benchmark_with_the_picked_client()
    {
        var generation = Guid.NewGuid();
        var dashboard = Blank<BenchmarkDashboardResponse>();
        var run = Blank<BenchmarkRunSummary>();
        var created = Blank<BenchmarkCaseResponse>();
        var request = new CreateManualBenchmarkCaseRequest("q?", Guid.NewGuid(), Guid.NewGuid());
        _benchmark.Setup(b => b.GetDashboardAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(dashboard);
        _benchmark.Setup(b => b.StartRunAsync(_clientId, "generation", generation, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _benchmark.Setup(b => b.CreateManualCaseAsync(_clientId, request, It.IsAny<CancellationToken>())).ReturnsAsync(created);
        _benchmark.Setup(b => b.ListCasesAsync(_clientId, new BenchmarkCaseFilter("manual", null), 25, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Blank<BenchmarkCaseListResponse>());

        Assert.Same(dashboard, await Sut().GetDashboardAsync(_clientId, default));
        Assert.Same(run, await Sut().StartRunAsync(_clientId, new StartBenchmarkRunRequest("generation", generation), default));
        Assert.Same(created, await Sut().CreateManualCaseAsync(_clientId, request, default));
        await Sut().ListCasesAsync(_clientId, new BenchmarkCaseFilter("manual", null), 25, 50, default);
        _benchmark.Verify(b => b.ListCasesAsync(_clientId, new BenchmarkCaseFilter("manual", null), 25, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Records_that_are_not_the_clients_are_404()
    {
        var id = Guid.NewGuid();
        var sut = Sut();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.GetCaseAsync(_clientId, id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.UpdateCaseQuestionAsync(_clientId, id, "q", default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.SetReviewedAsync(_clientId, id, true, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.DeleteCaseAsync(_clientId, id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.ListSourceChunksAsync(_clientId, id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.GetGenerationAsync(_clientId, id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.GetRunAsync(_clientId, id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.ListResultsAsync(_clientId, id, null, 0, 100, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.GetRunGenerationBreakdownAsync(_clientId, id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.GetResultAsync(_clientId, id, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Found_records_come_back_and_delete_succeeds()
    {
        var id = Guid.NewGuid();
        var detail = Blank<BenchmarkCaseDetailResponse>();
        _benchmark.Setup(b => b.GetCaseAsync(_clientId, id, It.IsAny<CancellationToken>())).ReturnsAsync(detail);
        _benchmark.Setup(b => b.DeleteCaseAsync(_clientId, id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.Same(detail, await Sut().GetCaseAsync(_clientId, id, default));
        await Sut().DeleteCaseAsync(_clientId, id, default);
        _benchmark.Verify(b => b.DeleteCaseAsync(_clientId, id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancelling_a_generation_maps_each_outcome()
    {
        var id = Guid.NewGuid();
        var summary = Blank<BenchmarkGenerationSummary>();
        _benchmark.SetupSequence(b => b.CancelGenerationAsync(_clientId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerationCancelResult(GenerationCancelStatus.NotFound, null))
            .ReturnsAsync(new GenerationCancelResult(GenerationCancelStatus.NotPending, summary))
            .ReturnsAsync(new GenerationCancelResult(GenerationCancelStatus.Cancelled, summary));
        var sut = Sut();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.CancelGenerationAsync(_clientId, id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CancelGenerationAsync(_clientId, id, default));
        Assert.Same(summary, await sut.CancelGenerationAsync(_clientId, id, default));
    }
}
