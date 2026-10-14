using System.Runtime.CompilerServices;
using PlasticSurgery.Business.Services.PlatformAdmin;

namespace PlasticSurgery.Tests.Services;

/// <summary>The admin portal's benchmark entry point: checks the clinic, delegates with that clinic id, and turns
/// "not this clinic's" answers into KeyNotFoundException (404).</summary>
public class KnowledgeBenchmarkAdminServiceTests
{
    private readonly Mock<IKnowledgeBenchmarkService> _benchmark = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Guid _clinicId = Guid.NewGuid();

    public KnowledgeBenchmarkAdminServiceTests()
    {
        _clinics.Setup(c => c.GetReadOnlyAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new Clinic { Id = _clinicId });
    }

    private KnowledgeBenchmarkAdminService Sut() => new(_benchmark.Object, _clinics.Object);

    private static T Blank<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    [Fact]
    public async Task An_unknown_clinic_is_404_and_the_benchmark_is_never_called()
    {
        var other = Guid.NewGuid();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().GetDashboardAsync(other, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().StartGenerationAsync(other, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().StartRunAsync(other, new StartBenchmarkRunRequest("all"), default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().CreateManualCaseAsync(other, Blank<CreateManualBenchmarkCaseRequest>(), default));
        _benchmark.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reads_and_writes_go_to_the_benchmark_with_the_picked_clinic()
    {
        var generation = Guid.NewGuid();
        var dashboard = Blank<BenchmarkDashboardResponse>();
        var run = Blank<BenchmarkRunSummary>();
        var created = Blank<BenchmarkCaseResponse>();
        var request = new CreateManualBenchmarkCaseRequest("q?", Guid.NewGuid(), Guid.NewGuid());
        _benchmark.Setup(b => b.GetDashboardAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(dashboard);
        _benchmark.Setup(b => b.StartRunAsync(_clinicId, "generation", generation, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _benchmark.Setup(b => b.CreateManualCaseAsync(_clinicId, request, It.IsAny<CancellationToken>())).ReturnsAsync(created);
        _benchmark.Setup(b => b.ListCasesAsync(_clinicId, new BenchmarkCaseFilter("manual", null), 25, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Blank<BenchmarkCaseListResponse>());

        Assert.Same(dashboard, await Sut().GetDashboardAsync(_clinicId, default));
        Assert.Same(run, await Sut().StartRunAsync(_clinicId, new StartBenchmarkRunRequest("generation", generation), default));
        Assert.Same(created, await Sut().CreateManualCaseAsync(_clinicId, request, default));
        await Sut().ListCasesAsync(_clinicId, new BenchmarkCaseFilter("manual", null), 25, 50, default);
        _benchmark.Verify(b => b.ListCasesAsync(_clinicId, new BenchmarkCaseFilter("manual", null), 25, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Records_that_are_not_the_clinics_are_404()
    {
        var id = Guid.NewGuid();
        var sut = Sut();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.GetCaseAsync(_clinicId, id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.UpdateCaseQuestionAsync(_clinicId, id, "q", default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.SetReviewedAsync(_clinicId, id, true, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.DeleteCaseAsync(_clinicId, id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.ListSourceChunksAsync(_clinicId, id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.GetGenerationAsync(_clinicId, id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.GetRunAsync(_clinicId, id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.ListResultsAsync(_clinicId, id, null, 0, 100, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.GetRunGenerationBreakdownAsync(_clinicId, id, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.GetResultAsync(_clinicId, id, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Found_records_come_back_and_delete_succeeds()
    {
        var id = Guid.NewGuid();
        var detail = Blank<BenchmarkCaseDetailResponse>();
        _benchmark.Setup(b => b.GetCaseAsync(_clinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(detail);
        _benchmark.Setup(b => b.DeleteCaseAsync(_clinicId, id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.Same(detail, await Sut().GetCaseAsync(_clinicId, id, default));
        await Sut().DeleteCaseAsync(_clinicId, id, default);
        _benchmark.Verify(b => b.DeleteCaseAsync(_clinicId, id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancelling_a_generation_maps_each_outcome()
    {
        var id = Guid.NewGuid();
        var summary = Blank<BenchmarkGenerationSummary>();
        _benchmark.SetupSequence(b => b.CancelGenerationAsync(_clinicId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerationCancelResult(GenerationCancelStatus.NotFound, null))
            .ReturnsAsync(new GenerationCancelResult(GenerationCancelStatus.NotPending, summary))
            .ReturnsAsync(new GenerationCancelResult(GenerationCancelStatus.Cancelled, summary));
        var sut = Sut();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => sut.CancelGenerationAsync(_clinicId, id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CancelGenerationAsync(_clinicId, id, default));
        Assert.Same(summary, await sut.CancelGenerationAsync(_clinicId, id, default));
    }
}
