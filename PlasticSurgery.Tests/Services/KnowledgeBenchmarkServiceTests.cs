using Microsoft.Extensions.Logging.Abstractions;
using PlasticSurgery.Business.Engines.KnowledgeBenchmark;
using PlasticSurgery.Business.Services.Knowledge;

namespace PlasticSurgery.Tests.Services;

public class KnowledgeBenchmarkServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IKnowledgeBenchmarkRepository> _repo = new();
    private readonly Mock<IKnowledgeDocumentRepository> _documents = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IUnitOfWorkTransaction> _tx = new();
    private readonly Mock<IKnowledgeSearchService> _search = new();
    private readonly Mock<IKnowledgeSettingsService> _settings = new();
    private readonly Mock<IKnowledgeBenchmarkGeneratorClient> _generator = new();
    private readonly Mock<IKnowledgeBenchmarkRunQueue> _queue = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly List<KnowledgeRetrievalBenchmarkCase> _cases = new();
    private readonly List<BenchmarkChunkInfo> _liveChunks = new();
    private readonly List<BenchmarkDocumentState> _docStates = new();

    public KnowledgeBenchmarkServiceTests()
    {
        _config.SetupGet(c => c.BenchmarkGenerationSampleSize).Returns(5);
        _config.SetupGet(c => c.BenchmarkSamplePoolSize).Returns(20);
        _config.SetupGet(c => c.BenchmarkMinSourceChunkChars).Returns(10);
        _config.SetupGet(c => c.BenchmarkMaxConsecutiveErrors).Returns(2);
        _config.SetupGet(c => c.BenchmarkStaleRunHours).Returns(1);
        _config.SetupGet(c => c.BenchmarkGenerationTimeoutMinutes).Returns(10);
        _config.SetupGet(c => c.BenchmarkMaxQuestionsPerChunk).Returns(2);
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);
        _settings.Setup(s => s.GetAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeSettingsResponse("m", 8, "cosine", "none", 200, 20, 5, 0.3, DateTimeOffset.UtcNow));
        _repo.Setup(r => r.GetActiveChunkStatsAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new ActiveChunkStats(10, 812.4));
        _repo.Setup(r => r.ListAllCasesAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _cases.ToList());
        _repo.Setup(r => r.ListChunksAsync(_clinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> ids, CancellationToken _) => _liveChunks.Where(c => ids.Contains(c.ChunkId)).ToList());
        _repo.Setup(r => r.ListActiveChunksAsync(_clinicId, It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid>? ids, CancellationToken _) => _liveChunks.Where(c => ids is null || ids.Contains(c.ChunkId)).ToList());
        _repo.Setup(r => r.ListDocumentStatesAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _docStates.ToList());
        _repo.Setup(r => r.ListCoveredSourceHashesAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        _repo.Setup(r => r.ListQuestionsForChunksAsync(_clinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<(Guid, string)>());
        _repo.Setup(r => r.CountCasesByGenerationAsync(_clinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<Guid, int>());
        _repo.Setup(r => r.ListCompletedResultRanksForGenerationsAsync(_clinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<BenchmarkResultRank>());
        _repo.Setup(r => r.ListResultsForCasesAsync(_clinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeRetrievalBenchmarkResult>());
        _repo.Setup(r => r.ListCasesAsync(_clinicId, It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (_cases.ToList() as IReadOnlyList<KnowledgeRetrievalBenchmarkCase>, _cases.Count));
        _generator.SetupGet(g => g.IsConfigured).Returns(true);
    }

    private KnowledgeBenchmarkService Sut() => new(_repo.Object, _documents.Object, _uow.Object, _search.Object, _settings.Object,
        _generator.Object, new KnowledgeBenchmarkScorer(), _queue.Object, NullLogger<KnowledgeBenchmarkService>.Instance, _config.Object);

    private (Guid Doc, Guid Chunk) LiveChunk(string content = "The consultation costs fifty dollars and lasts thirty minutes.", string title = "Pricing", bool docActive = true)
    {
        var doc = Guid.NewGuid();
        var chunk = Guid.NewGuid();
        _liveChunks.Add(new BenchmarkChunkInfo(chunk, doc, content, title));
        _docStates.Add(new BenchmarkDocumentState(doc, title, docActive));
        return (doc, chunk);
    }

    private KnowledgeRetrievalBenchmarkCase Case((Guid Doc, Guid Chunk) at, string question = "How much is a consultation?", string? content = "The consultation costs fifty dollars and lasts thirty minutes.")
    {
        var c = new KnowledgeRetrievalBenchmarkCase
        {
            Id = Guid.NewGuid(), ClinicId = _clinicId, Question = question, ExpectedDocumentId = at.Doc, ExpectedChunkId = at.Chunk,
            SourceChunkHash = content is null ? null : KnowledgeBenchmarkService.Sha256Hex(content), SourceDocumentTitle = "Pricing"
        };
        _cases.Add(c);
        _repo.Setup(r => r.GetCaseAsync(_clinicId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        return c;
    }

    // ---------------------------------------------------------------- stale refresh

    [Fact]
    public async Task A_case_whose_chunk_is_unchanged_stays_fresh()
    {
        var c = Case(LiveChunk());
        await Sut().ListCasesAsync(_clinicId, new BenchmarkCaseFilter(), 0, 10);
        Assert.False(c.IsStale);
    }

    [Theory]
    [InlineData("changed", BenchmarkStaleReason.ChunkChanged)]
    [InlineData("missing", BenchmarkStaleReason.ChunkMissing)]
    [InlineData("inactive", BenchmarkStaleReason.DocumentInactive)]
    public async Task Cases_go_stale_for_the_right_reason(string scenario, string reason)
    {
        var at = LiveChunk(docActive: scenario != "inactive");
        var c = Case(at);
        if (scenario == "changed") _liveChunks[0] = _liveChunks[0] with { Content = "Completely different text now." };
        if (scenario == "missing") { _liveChunks.Clear(); }
        await Sut().ListCasesAsync(_clinicId, new BenchmarkCaseFilter(), 0, 10);
        Assert.True(c.IsStale);
        Assert.Equal(reason, c.StaleReason);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task A_moved_chunk_with_identical_text_is_relinked_instead_of_going_stale()
    {
        var old = LiveChunk(title: "Old");
        var c = Case(old);
        _liveChunks.Clear();
        _docStates.Clear();
        var moved = LiveChunk(title: "New title");
        await Sut().ListCasesAsync(_clinicId, new BenchmarkCaseFilter(), 0, 10);
        Assert.False(c.IsStale);
        Assert.Equal((moved.Doc, moved.Chunk, "New title"), (c.ExpectedDocumentId, c.ExpectedChunkId, c.SourceDocumentTitle));
    }

    [Fact]
    public async Task Duplicate_text_in_two_chunks_is_ambiguous_so_the_case_stays_stale()
    {
        var old = LiveChunk();
        var c = Case(old);
        _liveChunks.Clear();
        _docStates.Clear();
        LiveChunk();
        LiveChunk();
        await Sut().ListCasesAsync(_clinicId, new BenchmarkCaseFilter(), 0, 10);
        Assert.True(c.IsStale);
    }

    [Fact]
    public async Task Renamed_documents_update_the_displayed_title_and_failures_never_break_the_caller()
    {
        var at = LiveChunk(title: "Pricing v2");
        var c = Case(at);
        await Sut().GetCaseAsync(_clinicId, c.Id);
        Assert.Equal("Pricing v2", c.SourceDocumentTitle);

        _repo.Setup(r => r.ListAllCasesAsync(_clinicId, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));
        Assert.Null(await Sut().GetCaseAsync(_clinicId, Guid.NewGuid()));
        _repo.Verify(r => r.DetachAllCases(), Times.Once);
    }

    // ---------------------------------------------------------------- manual cases

    [Fact]
    public async Task Manual_case_is_stored_reviewed_with_a_hash_and_preview()
    {
        var at = LiveChunk();
        _repo.Setup(r => r.GetActiveChunkAsync(_clinicId, at.Doc, at.Chunk, It.IsAny<CancellationToken>())).ReturnsAsync(_liveChunks[0]);
        KnowledgeRetrievalBenchmarkCase? added = null;
        _repo.Setup(r => r.AddCase(It.IsAny<KnowledgeRetrievalBenchmarkCase>())).Callback<KnowledgeRetrievalBenchmarkCase>(c => added = c);
        var r = await Sut().CreateManualCaseAsync(_clinicId, new CreateManualBenchmarkCaseRequest("  How   much\nis it? ", at.Doc, at.Chunk));
        Assert.Equal(("How much is it?", BenchmarkCaseType.Manual, true), (added!.Question, added.CaseType, added.IsReviewed));
        Assert.Equal(KnowledgeBenchmarkService.Sha256Hex(_liveChunks[0].Content), added.SourceChunkHash);
        Assert.Equal(added.Id, r.Id);
    }

    [Fact]
    public async Task Manual_case_validation_and_duplicates()
    {
        var at = LiveChunk();
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateManualCaseAsync(_clinicId, new CreateManualBenchmarkCaseRequest("  ", at.Doc, at.Chunk)));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateManualCaseAsync(_clinicId, new CreateManualBenchmarkCaseRequest(new string('q', 501), at.Doc, at.Chunk)));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateManualCaseAsync(_clinicId, new CreateManualBenchmarkCaseRequest("q?", at.Doc, at.Chunk))); // chunk not active
        _repo.Setup(r => r.GetActiveChunkAsync(_clinicId, at.Doc, at.Chunk, It.IsAny<CancellationToken>())).ReturnsAsync(_liveChunks[0]);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DuplicateRecordException(new Exception("dup")));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateManualCaseAsync(_clinicId, new CreateManualBenchmarkCaseRequest("q?", at.Doc, at.Chunk)));
        _repo.Verify(r => r.DetachCase(It.IsAny<KnowledgeRetrievalBenchmarkCase>()), Times.Once);
    }

    [Fact]
    public async Task Edit_review_and_delete_cases()
    {
        var c = Case(LiveChunk());
        Assert.Null(await Sut().UpdateCaseQuestionAsync(_clinicId, Guid.NewGuid(), "x?"));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateCaseQuestionAsync(_clinicId, c.Id, " "));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateCaseQuestionAsync(_clinicId, c.Id, new string('q', 501)));
        var updated = await Sut().UpdateCaseQuestionAsync(_clinicId, c.Id, "  New   question ");
        Assert.Equal("New question", updated!.Question);

        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DuplicateRecordException(new Exception("dup")));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateCaseQuestionAsync(_clinicId, c.Id, "another"));
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        Assert.Null(await Sut().SetReviewedAsync(_clinicId, Guid.NewGuid(), true));
        Assert.True((await Sut().SetReviewedAsync(_clinicId, c.Id, true))!.IsReviewed);
        Assert.NotNull(c.ReviewedAt);
        Assert.False((await Sut().SetReviewedAsync(_clinicId, c.Id, false))!.IsReviewed);
        Assert.Null(c.ReviewedAt);

        _repo.Setup(r => r.DeleteCaseAsync(_clinicId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        Assert.True(await Sut().DeleteCaseAsync(_clinicId, c.Id));
        Assert.False(await Sut().DeleteCaseAsync(_clinicId, Guid.NewGuid()));
    }

    [Fact]
    public async Task Source_chunks_listing_needs_an_existing_document()
    {
        Assert.Null(await Sut().ListSourceChunksAsync(_clinicId, Guid.NewGuid()));
        var doc = Guid.NewGuid();
        _documents.Setup(d => d.ExistsAsync(_clinicId, doc, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.ListDocumentChunksAsync(_clinicId, doc, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeChunk> { new() { Id = Guid.NewGuid(), ChunkIndex = 0, Content = new string('x', 400) } });
        var chunks = await Sut().ListSourceChunksAsync(_clinicId, doc);
        Assert.Equal(161, Assert.Single(chunks!).Preview.Length); // 160 chars + ellipsis
    }

    // ---------------------------------------------------------------- starting a run

    [Theory]
    [InlineData("bogus", false)]
    [InlineData("generation", false)]
    [InlineData("all", true)]
    public async Task Run_scope_and_generation_id_must_agree(string scope, bool withGeneration)
    {
        Guid? gid = withGeneration ? Guid.NewGuid() : null;
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().StartRunAsync(_clinicId, scope, gid));
    }

    [Fact]
    public async Task Unknown_generation_blocks_the_run()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().StartRunAsync(_clinicId, "generation", Guid.NewGuid()));
    }

    [Fact]
    public async Task A_run_in_progress_or_an_empty_or_fully_stale_scope_cannot_start()
    {
        _repo.Setup(r => r.HasRunInProgressAsync(_clinicId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<BenchmarkRunInProgressException>(() => Sut().StartRunAsync(_clinicId, null));
        _repo.Setup(r => r.HasRunInProgressAsync(_clinicId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        _repo.Setup(r => r.CountCasesInScopeAsync(_clinicId, "all", null, It.IsAny<CancellationToken>())).ReturnsAsync((0, 0));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().StartRunAsync(_clinicId, "all"));
        _repo.Setup(r => r.CountCasesInScopeAsync(_clinicId, "reviewed", null, It.IsAny<CancellationToken>())).ReturnsAsync((0, 0));
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().StartRunAsync(_clinicId, "reviewed"));
        Assert.Contains("reviewed", ex.Message);
        _repo.Setup(r => r.CountCasesInScopeAsync(_clinicId, "all", null, It.IsAny<CancellationToken>())).ReturnsAsync((4, 4));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().StartRunAsync(_clinicId, "all"));
    }

    [Fact]
    public async Task Starting_a_run_snapshots_the_settings_and_queues_it()
    {
        _repo.Setup(r => r.CountCasesInScopeAsync(_clinicId, "generated", null, It.IsAny<CancellationToken>())).ReturnsAsync((6, 1));
        KnowledgeRetrievalBenchmarkRun? added = null;
        _repo.Setup(r => r.AddRun(It.IsAny<KnowledgeRetrievalBenchmarkRun>())).Callback<KnowledgeRetrievalBenchmarkRun>(r => added = r);
        var summary = await Sut().StartRunAsync(_clinicId, " GENERATED ");
        Assert.Equal((BenchmarkRunStatus.Pending, 6, 1, "m", 5, 0.3, 10), (added!.Status, added.TotalCases, added.StaleCases, added.EmbeddingModel, added.TopK, added.MinimumSimilarity, added.IndexedChunkCount));
        Assert.Equal(812d, added.AvgChunkChars);
        _queue.Verify(q => q.Enqueue(added.Id), Times.Once);
        Assert.Equal(added.Id, summary.Id);
    }

    // ---------------------------------------------------------------- executing a run

    private KnowledgeRetrievalBenchmarkRun PendingRun(string scope = "all")
    {
        var run = new KnowledgeRetrievalBenchmarkRun { Id = Guid.NewGuid(), ClinicId = _clinicId, CaseScope = scope, Status = BenchmarkRunStatus.Pending };
        _repo.Setup(r => r.GetRunAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _repo.Setup(r => r.ListCasesInScopeAsync(_clinicId, scope, null, It.IsAny<CancellationToken>())).ReturnsAsync(() => _cases.ToList());
        return run;
    }

    private static RankedKnowledgeChunk Hit(Guid doc, Guid chunk, double score, bool meets = true) => new(chunk, doc, "T", "faq", "content text", score, meets);

    [Fact]
    public async Task Run_that_is_missing_or_not_pending_is_ignored()
    {
        await Sut().ExecuteRunAsync(Guid.NewGuid());
        var run = PendingRun();
        run.Status = BenchmarkRunStatus.Completed;
        await Sut().ExecuteRunAsync(run.Id);
        _search.Verify(s => s.SearchRankedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execution_scores_each_case_stores_results_and_completes_with_metrics()
    {
        var a = LiveChunk(); var b = LiveChunk("Recovery takes about two weeks before you can return to work.", "Recovery");
        var hitCase = Case(a);
        var missCase = Case(b, "How long is recovery?", "Recovery takes about two weeks before you can return to work.");
        var staleCase = Case(LiveChunk("x", "Gone"), "Stale question?", content: "something else entirely");
        staleCase.IsStale = true;
        staleCase.StaleReason = BenchmarkStaleReason.ChunkChanged;
        _repo.Setup(r => r.ListAllCasesAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeRetrievalBenchmarkCase>());
        var run = PendingRun();
        _search.Setup(s => s.SearchRankedAsync(_clinicId, hitCase.Question, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RankedKnowledgeChunk> { Hit(a.Doc, a.Chunk, 0.9) });
        _search.Setup(s => s.SearchRankedAsync(_clinicId, missCase.Question, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RankedKnowledgeChunk> { Hit(Guid.NewGuid(), Guid.NewGuid(), 0.8), Hit(b.Doc, b.Chunk, 0.1, meets: false) });
        var results = new List<KnowledgeRetrievalBenchmarkResult>();
        _repo.Setup(r => r.AddResult(It.IsAny<KnowledgeRetrievalBenchmarkResult>())).Callback<KnowledgeRetrievalBenchmarkResult>(results.Add);

        await Sut().ExecuteRunAsync(run.Id);

        Assert.Equal(BenchmarkRunStatus.Completed, run.Status);
        Assert.Equal((3, 3, 2, 1, 0), (run.TotalCases, run.ProcessedCases, run.ScoredCases, run.StaleCases, run.ErrorCases));
        Assert.Equal(0.5, run.ChunkTop1Accuracy);
        Assert.Equal(0.5, run.ChunkMrr);
        Assert.Equal(BenchmarkClassification.ExactChunkHit, results.Single(r => r.Question == hitCase.Question).ResultClassification);
        var miss = results.Single(r => r.Question == missCase.Question);
        Assert.Equal((BenchmarkClassification.Miss, true, 1), (miss.ResultClassification, miss.ExpectedBelowThreshold, miss.ReturnedCount));
        Assert.Equal(BenchmarkClassification.StaleCase, results.Single(r => r.Question == staleCase.Question).ResultClassification);
        Assert.NotNull(run.AverageLatencyMs);
        _repo.Verify(r => r.DetachResult(It.IsAny<KnowledgeRetrievalBenchmarkResult>()), Times.Exactly(3));
    }

    [Fact]
    public async Task A_case_that_errors_is_recorded_and_the_run_still_completes_with_a_note()
    {
        var at = LiveChunk();
        Case(at);
        Case(LiveChunk("Another chunk with enough text in it.", "B"), "second?", "Another chunk with enough text in it.");
        var run = PendingRun();
        _search.SetupSequence(s => s.SearchRankedAsync(_clinicId, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("openai 500"))
            .ReturnsAsync(new List<RankedKnowledgeChunk>());
        var results = new List<KnowledgeRetrievalBenchmarkResult>();
        _repo.Setup(r => r.AddResult(It.IsAny<KnowledgeRetrievalBenchmarkResult>())).Callback<KnowledgeRetrievalBenchmarkResult>(results.Add);
        await Sut().ExecuteRunAsync(run.Id);
        Assert.Equal(BenchmarkRunStatus.Completed, run.Status);
        Assert.Contains("1 case(s) errored", run.ErrorSummary);
        Assert.Contains(results, r => r.ResultClassification == BenchmarkClassification.Error && r.ErrorMessage == "openai 500");
    }

    [Fact]
    public async Task Consecutive_errors_abort_the_run()
    {
        for (var i = 0; i < 4; i++) Case(LiveChunk($"Chunk number {i} with sufficient text.", $"D{i}"), $"q{i}?", $"Chunk number {i} with sufficient text.");
        var run = PendingRun();
        _search.Setup(s => s.SearchRankedAsync(_clinicId, It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));
        await Sut().ExecuteRunAsync(run.Id);
        Assert.Equal(BenchmarkRunStatus.Failed, run.Status);
        Assert.Equal(2, run.ProcessedCases);
        Assert.Contains("2 retrieval errors in a row", run.ErrorSummary);
    }

    [Fact]
    public async Task An_unexpected_failure_marks_the_run_failed_and_cancellation_propagates()
    {
        var run = PendingRun();
        _repo.Setup(r => r.ListCasesInScopeAsync(_clinicId, "all", null, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db exploded"));
        await Sut().ExecuteRunAsync(run.Id);
        Assert.Equal(BenchmarkRunStatus.Failed, run.Status);
        Assert.Contains("db exploded", run.ErrorSummary);
        _uow.Verify(u => u.DiscardChanges(), Times.Once);

        var second = PendingRun();
        _repo.Setup(r => r.ListCasesInScopeAsync(_clinicId, "all", null, It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => Sut().ExecuteRunAsync(second.Id));
    }

    [Fact]
    public async Task Interrupted_runs_are_failed_on_startup_and_pending_ones_returned_for_requeueing()
    {
        var running = new KnowledgeRetrievalBenchmarkRun { Id = Guid.NewGuid(), Status = BenchmarkRunStatus.Running };
        _repo.Setup(r => r.ListRunsInStatusAsync(BenchmarkRunStatus.Running, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeRetrievalBenchmarkRun> { running });
        var pending = Guid.NewGuid();
        _repo.Setup(r => r.ListRunIdsInStatusAsync(BenchmarkRunStatus.Pending, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Guid> { pending });
        Assert.Equal([pending], await Sut().RecoverInterruptedRunsAsync());
        Assert.Equal(BenchmarkRunStatus.Failed, running.Status);
        Assert.Contains("restart", running.ErrorSummary);
    }

    // ---------------------------------------------------------------- reads

    [Fact]
    public async Task Dashboard_combines_counts_runs_and_generation_state()
    {
        _repo.Setup(r => r.GetCaseCountsAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new BenchmarkCaseCounts(10, 7, 3, 4, 1));
        var completed = new KnowledgeRetrievalBenchmarkRun { Id = Guid.NewGuid(), Status = BenchmarkRunStatus.Completed };
        var failed = new KnowledgeRetrievalBenchmarkRun { Id = Guid.NewGuid(), Status = BenchmarkRunStatus.Failed };
        _repo.Setup(r => r.GetLatestRunAsync(_clinicId, false, It.IsAny<CancellationToken>())).ReturnsAsync(failed);
        _repo.Setup(r => r.GetLatestRunAsync(_clinicId, true, It.IsAny<CancellationToken>())).ReturnsAsync(completed);
        var pendingGen = Guid.NewGuid();
        _repo.Setup(r => r.GetPendingGenerationIdAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(pendingGen);
        var d = await Sut().GetDashboardAsync(_clinicId);
        Assert.Equal((10, 7, 3, 4, 1), (d.TotalCases, d.GeneratedCases, d.ManualCases, d.ReviewedCases, d.StaleCases));
        Assert.Equal((failed.Id, completed.Id), (d.LatestRun!.Id, d.LatestCompletedRun!.Id));
        Assert.True(d.GenerationInProgress);
        Assert.Equal(pendingGen, d.PendingGenerationId);
        Assert.True(d.GeneratorConfigured);
    }

    [Fact]
    public async Task Run_and_result_lookups()
    {
        var run = new KnowledgeRetrievalBenchmarkRun { Id = Guid.NewGuid(), Status = BenchmarkRunStatus.Completed };
        _repo.Setup(r => r.ListRunsAsync(_clinicId, 100, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeRetrievalBenchmarkRun> { run });
        Assert.Single(await Sut().ListRunsAsync(_clinicId, 5000));
        _repo.Setup(r => r.GetRunReadOnlyAsync(_clinicId, run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        Assert.NotNull(await Sut().GetRunAsync(_clinicId, run.Id));
        Assert.Null(await Sut().GetRunAsync(_clinicId, Guid.NewGuid()));

        Assert.Null(await Sut().ListResultsAsync(_clinicId, Guid.NewGuid(), null, 0, 10));
        _repo.Setup(r => r.RunExistsAsync(_clinicId, run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _repo.Setup(r => r.ListRunResultsAsync(_clinicId, run.Id, "MISS", 0, 500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<KnowledgeRetrievalBenchmarkResult> { new() { Id = Guid.NewGuid(), BenchmarkRunId = run.Id, Question = "q" } } as IReadOnlyList<KnowledgeRetrievalBenchmarkResult>, 1));
        var list = await Sut().ListResultsAsync(_clinicId, run.Id, " miss ", -3, 9999);
        Assert.Equal(1, list!.TotalCount);

        Assert.Null(await Sut().GetResultAsync(_clinicId, run.Id, Guid.NewGuid()));
    }

    // ---------------------------------------------------------------- generation

    [Fact]
    public async Task Generation_needs_a_configured_generator_and_no_pending_generation()
    {
        _generator.SetupGet(g => g.IsConfigured).Returns(false);
        await Assert.ThrowsAsync<BenchmarkGeneratorNotConfiguredException>(() => Sut().StartGenerationAsync(_clinicId));
        _generator.SetupGet(g => g.IsConfigured).Returns(true);
        _repo.Setup(r => r.GetPendingGenerationIdAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        await Assert.ThrowsAsync<BenchmarkGenerationInProgressException>(() => Sut().StartGenerationAsync(_clinicId));
    }

    [Fact]
    public async Task Generation_with_nothing_to_sample_reports_none()
    {
        _repo.Setup(r => r.SampleUncoveredChunksAsync(_clinicId, 10, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<BenchmarkChunkInfo>());
        var r = await Sut().StartGenerationAsync(_clinicId);
        Assert.Equal(("none", 0, null), (r.Status, r.ChunksSent, r.GenerationId));
    }

    [Fact]
    public async Task Sampling_skips_covered_and_duplicate_chunks_and_caps_the_sample()
    {
        var pool = Enumerable.Range(0, 8).Select(i => new BenchmarkChunkInfo(Guid.NewGuid(), Guid.NewGuid(), $"unique content number {i}", "T")).ToList();
        pool.Add(pool[1] with { ChunkId = Guid.NewGuid() }); // same text, different chunk
        _repo.Setup(r => r.SampleUncoveredChunksAsync(_clinicId, 10, 20, It.IsAny<CancellationToken>())).ReturnsAsync(pool);
        _repo.Setup(r => r.ListCoveredSourceHashesAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { KnowledgeBenchmarkService.Sha256Hex(pool[0].Content) });
        _generator.Setup(g => g.SendAsync(_clinicId, It.IsAny<Guid>(), It.IsAny<IReadOnlyList<BenchmarkSourceChunk>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GeneratorSendResult(null, null));
        var r = await Sut().StartGenerationAsync(_clinicId);
        Assert.Equal((BenchmarkGenerationStatus.Pending, 5), (r.Status, r.ChunksSent));
        _generator.Verify(g => g.SendAsync(_clinicId, r.GenerationId!.Value, It.Is<IReadOnlyList<BenchmarkSourceChunk>>(c => c.Count == 5 && c.All(x => x.ChunkId != pool[0].ChunkId)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_generator_failure_marks_the_generation_failed_and_rethrows()
    {
        _repo.Setup(r => r.SampleUncoveredChunksAsync(_clinicId, 10, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<BenchmarkChunkInfo> { new(Guid.NewGuid(), Guid.NewGuid(), "some chunk content", "T") });
        _generator.Setup(g => g.SendAsync(_clinicId, It.IsAny<Guid>(), It.IsAny<IReadOnlyList<BenchmarkSourceChunk>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new BenchmarkGenerationException("n8n said no"));
        await Assert.ThrowsAsync<BenchmarkGenerationException>(() => Sut().StartGenerationAsync(_clinicId));
        _repo.Verify(r => r.FailPendingGenerationAsync(It.IsAny<Guid>(), "n8n said no", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private KnowledgeRetrievalBenchmarkGeneration PendingGeneration(IEnumerable<(Guid Doc, Guid Chunk)> sent, DateTimeOffset? createdAt = null)
    {
        var rows = sent.Select(s => new { DocumentId = s.Doc, ChunkId = s.Chunk, DocumentTitle = "Pricing" });
        var g = new KnowledgeRetrievalBenchmarkGeneration
        {
            Id = Guid.NewGuid(), ClinicId = _clinicId, Status = BenchmarkGenerationStatus.Pending, ChunksSent = sent.Count(),
            SentChunksJson = System.Text.Json.JsonSerializer.Serialize(rows, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        };
        _repo.Setup(r => r.GetGenerationReadOnlyAsync(g.Id, It.IsAny<CancellationToken>())).ReturnsAsync(g);
        _repo.Setup(r => r.GetGenerationAsync(g.Id, It.IsAny<CancellationToken>())).ReturnsAsync(g);
        _repo.Setup(r => r.ClaimPendingGenerationAsync(g.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return g;
    }

    private static RawGeneratedQuestion Q(Guid doc, Guid chunk, string q) => new(doc.ToString(), chunk.ToString(), q);

    [Fact]
    public async Task Received_questions_are_validated_deduplicated_capped_and_stored_as_unreviewed_cases()
    {
        var at = LiveChunk();
        var other = LiveChunk("Second chunk content that is long enough.", "Other");
        var g = PendingGeneration([at, other]);
        List<KnowledgeRetrievalBenchmarkCase>? added = null;
        _repo.Setup(r => r.AddCases(It.IsAny<IEnumerable<KnowledgeRetrievalBenchmarkCase>>())).Callback<IEnumerable<KnowledgeRetrievalBenchmarkCase>>(c => added = c.ToList());
        _repo.Setup(r => r.ListQuestionsForChunksAsync(_clinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<(Guid, string)> { (other.Chunk, "already there?") });

        var reply = new GeneratorResponse(g.Id.ToString(), new List<RawGeneratedQuestion>
        {
            Q(at.Doc, at.Chunk, "How much?"),
            Q(at.Doc, at.Chunk, "how   much?"),               // duplicate after normalising
            Q(at.Doc, at.Chunk, "How long?"),
            Q(at.Doc, at.Chunk, "Third question for same chunk?"), // over the per-chunk cap (2)
            Q(Guid.NewGuid(), Guid.NewGuid(), "Unknown chunk?"),
            Q(other.Doc, at.Chunk, "Wrong document?"),
            Q(other.Doc, other.Chunk, "already there?"),
            Q(other.Doc, other.Chunk, "   "),
            Q(other.Doc, other.Chunk, new string('q', 501)),
            new("nope", "nope", "Bad ids?")
        });
        var result = await Sut().ReceiveGenerationResultAsync(g.Id, reply, "{raw}", requireEcho: true);

        Assert.Equal(GenerationReceiveStatus.Completed, result.Status);
        Assert.Equal(["How much?", "How long?"], added!.Select(c => c.Question));
        Assert.All(added, c => Assert.Equal((BenchmarkCaseType.Generated, false, g.Id), (c.CaseType, c.IsReviewed, c.GenerationId)));
        Assert.Equal(8, result.Rejected!.Count);
        Assert.Equal((10, 2, 8), (g.QuestionsReturned, g.CasesCreated, g.RejectedCount));
        Assert.Equal("{raw}", g.RawResponse);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_chunk_that_changed_since_it_was_sent_is_rejected()
    {
        var at = LiveChunk();
        var g = PendingGeneration([at]);
        _liveChunks.Clear();
        var result = await Sut().ReceiveGenerationResultAsync(g.Id, new GeneratorResponse(g.Id.ToString(), [Q(at.Doc, at.Chunk, "Gone?")]), null, true);
        Assert.Contains(result.Rejected!, r => r.Reason.Contains("no longer exists"));
        Assert.Equal(0, g.CasesCreated);
    }

    [Fact]
    public async Task Receiving_for_unknown_finished_or_expired_generations()
    {
        var missing = await Sut().ReceiveGenerationResultAsync(Guid.NewGuid(), new GeneratorResponse(null, []), null, false);
        Assert.Equal(GenerationReceiveStatus.NotFound, missing.Status);

        var done = PendingGeneration([]);
        done.Status = BenchmarkGenerationStatus.Completed;
        Assert.Equal(GenerationReceiveStatus.AlreadyProcessed, (await Sut().ReceiveGenerationResultAsync(done.Id, new GeneratorResponse(null, []), null, false)).Status);

        var failed = PendingGeneration([]);
        failed.Status = BenchmarkGenerationStatus.Failed;
        failed.ErrorMessage = "earlier error";
        var r = await Sut().ReceiveGenerationResultAsync(failed.Id, new GeneratorResponse(null, []), null, false);
        Assert.Equal((GenerationReceiveStatus.NotAccepting, "earlier error"), (r.Status, r.Message));

        var old = PendingGeneration([], DateTimeOffset.UtcNow.AddHours(-2));
        var expired = await Sut().ReceiveGenerationResultAsync(old.Id, new GeneratorResponse(null, []), null, false);
        Assert.Equal(GenerationReceiveStatus.NotAccepting, expired.Status);
        _repo.Verify(x => x.ExpirePendingGenerationsAsync(_clinicId, It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Echo_rules_differ_between_the_inline_reply_and_the_callback()
    {
        var g = PendingGeneration([]);
        await Assert.ThrowsAsync<BenchmarkGenerationException>(() => Sut().ReceiveGenerationResultAsync(g.Id, new GeneratorResponse(null, []), null, requireEcho: true));
        await Assert.ThrowsAsync<BenchmarkGenerationException>(() => Sut().ReceiveGenerationResultAsync(g.Id, new GeneratorResponse(Guid.NewGuid().ToString(), []), null, requireEcho: true));
        _repo.Verify(r => r.FailPendingGenerationAsync(g.Id, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        await Assert.ThrowsAsync<ArgumentException>(() => Sut().ReceiveGenerationResultAsync(g.Id, new GeneratorResponse(Guid.NewGuid().ToString(), []), null, requireEcho: false));
        var ok = await Sut().ReceiveGenerationResultAsync(g.Id, new GeneratorResponse(null, []), null, requireEcho: false);
        Assert.Equal(GenerationReceiveStatus.Completed, ok.Status);
    }

    [Fact]
    public async Task Losing_the_claim_race_reports_the_current_state_and_rolls_back()
    {
        var g = PendingGeneration([]);
        _repo.Setup(r => r.ClaimPendingGenerationAsync(g.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var result = await Sut().ReceiveGenerationResultAsync(g.Id, new GeneratorResponse(g.Id.ToString(), []), null, true);
        Assert.Equal(GenerationReceiveStatus.NotAccepting, result.Status);
        _tx.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Immediate_replies_are_applied_during_start()
    {
        var at = LiveChunk();
        _repo.Setup(r => r.SampleUncoveredChunksAsync(_clinicId, 10, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<BenchmarkChunkInfo> { _liveChunks[0] });
        KnowledgeRetrievalBenchmarkGeneration? stored = null;
        _repo.Setup(r => r.AddGeneration(It.IsAny<KnowledgeRetrievalBenchmarkGeneration>())).Callback<KnowledgeRetrievalBenchmarkGeneration>(g =>
        {
            stored = g;
            _repo.Setup(x => x.GetGenerationReadOnlyAsync(g.Id, It.IsAny<CancellationToken>())).ReturnsAsync(g);
            _repo.Setup(x => x.GetGenerationAsync(g.Id, It.IsAny<CancellationToken>())).ReturnsAsync(g);
            _repo.Setup(x => x.ClaimPendingGenerationAsync(g.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        });
        _generator.Setup(g => g.SendAsync(_clinicId, It.IsAny<Guid>(), It.IsAny<IReadOnlyList<BenchmarkSourceChunk>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid gid, IReadOnlyList<BenchmarkSourceChunk> _, CancellationToken _) =>
                new GeneratorSendResult(new GeneratorResponse(gid.ToString(), [Q(at.Doc, at.Chunk, "How much?")]), "{}"));
        var r = await Sut().StartGenerationAsync(_clinicId);
        Assert.Equal((1, 1), (r.QuestionsReturned, r.CasesCreated));
        Assert.Equal(stored!.Id, r.GenerationId);
    }

    [Fact]
    public async Task Cancelling_a_generation()
    {
        var gid = Guid.NewGuid();
        Assert.Equal(GenerationCancelStatus.NotFound, (await Sut().CancelGenerationAsync(_clinicId, gid)).Status);
        _repo.Setup(r => r.GenerationExistsAsync(_clinicId, gid, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var g = new KnowledgeRetrievalBenchmarkGeneration { Id = gid, ClinicId = _clinicId, Status = BenchmarkGenerationStatus.Pending };
        _repo.Setup(r => r.GetGenerationReadOnlyAsync(_clinicId, gid, It.IsAny<CancellationToken>())).ReturnsAsync(g);
        Assert.Equal(GenerationCancelStatus.NotPending, (await Sut().CancelGenerationAsync(_clinicId, gid)).Status);
        _repo.Setup(r => r.CancelPendingGenerationAsync(_clinicId, gid, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        Assert.Equal(GenerationCancelStatus.Cancelled, (await Sut().CancelGenerationAsync(_clinicId, gid)).Status);
    }

    [Fact]
    public async Task Generation_lists_details_and_run_breakdown()
    {
        var gid = Guid.NewGuid();
        var g = new KnowledgeRetrievalBenchmarkGeneration
        {
            Id = gid, ClinicId = _clinicId, Status = BenchmarkGenerationStatus.Completed, CreatedAt = DateTimeOffset.UtcNow,
            RejectedJson = "[{\"question\":\"q\",\"reason\":\"dup\"}]", SentChunksJson = "[{\"documentId\":\"" + Guid.NewGuid() + "\",\"chunkId\":\"" + Guid.NewGuid() + "\",\"documentTitle\":\"T\"}]", RawResponse = "{}"
        };
        _repo.Setup(r => r.ListGenerationsAsync(_clinicId, 100, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeRetrievalBenchmarkGeneration> { g });
        _repo.Setup(r => r.GetGenerationReadOnlyAsync(_clinicId, gid, It.IsAny<CancellationToken>())).ReturnsAsync(g);
        _repo.Setup(r => r.CountCasesByGenerationAsync(_clinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<Guid, int> { [gid] = 4 });
        var runId = Guid.NewGuid();
        var runAt = DateTimeOffset.UtcNow;
        _repo.Setup(r => r.ListCompletedResultRanksForGenerationsAsync(_clinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BenchmarkResultRank> { new(gid, runId, runAt, BenchmarkClassification.ExactChunkHit, 1, 1), new(gid, runId, runAt, BenchmarkClassification.Miss, null, null), new(gid, runId, runAt, BenchmarkClassification.StaleCase, null, null) });

        var list = await Sut().ListGenerationsAsync(_clinicId, 1000);
        var summary = Assert.Single(list);
        Assert.Equal((4, 2, 0.5), (summary.CasesRemaining, summary.LatestScores!.ScoredCases, summary.LatestScores.ChunkTop1));

        var detail = await Sut().GetGenerationAsync(_clinicId, gid);
        Assert.Equal(("dup", 1, "{}"), (detail!.Rejected[0].Reason, detail.SentChunks.Count, detail.RawResponse));
        Assert.Null(await Sut().GetGenerationAsync(_clinicId, Guid.NewGuid()));

        Assert.Null(await Sut().GetRunGenerationBreakdownAsync(_clinicId, Guid.NewGuid()));
        var run = new KnowledgeRetrievalBenchmarkRun { Id = runId, CreatedAt = runAt };
        _repo.Setup(r => r.GetRunReadOnlyAsync(_clinicId, runId, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        _repo.Setup(r => r.ListRunResultRanksAsync(_clinicId, runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BenchmarkResultRank> { new(null, runId, runAt, BenchmarkClassification.Miss, null, null), new(gid, runId, runAt, BenchmarkClassification.ExactChunkHit, 1, 1) });
        _repo.Setup(r => r.GetGenerationRequestTimesAsync(_clinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<Guid, DateTimeOffset> { [gid] = runAt });
        var breakdown = await Sut().GetRunGenerationBreakdownAsync(_clinicId, runId);
        Assert.Equal([gid, (Guid?)null], breakdown!.Select(b => b.GenerationId)); // generations first, manual last
    }
}
