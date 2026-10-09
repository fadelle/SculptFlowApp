using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Persistence.Repositories.Knowledge;

namespace PlasticSurgery.Tests.Repositories;

public class KnowledgeBenchmarkRepositoryTests(PostgresFixture fixture) : RepoTestBase(fixture)
{
    private sealed record Setup(Clinic Clinic, KnowledgeDocument Doc, Guid Chunk1, Guid Chunk2);

    private async Task<Setup> SeedKnowledgeAsync(bool activeDoc = true)
    {
        var c = MakeClinic();
        var doc = MakeDocument(c.Id, "Pricing", activeDoc);
        await SeedAsync(c, doc);
        var c1 = await SeedChunkAsync(c.Id, doc.Id, 0, new string('a', 60) + " first chunk about prices");
        var c2 = await SeedChunkAsync(c.Id, doc.Id, 1, new string('b', 60) + " second chunk about hours");
        return new Setup(c, doc, c1, c2);
    }

    private static KnowledgeRetrievalBenchmarkCase Case(Setup s, string question, Action<KnowledgeRetrievalBenchmarkCase>? tweak = null, int minutesAgo = 0)
    {
        var x = new KnowledgeRetrievalBenchmarkCase
        {
            Id = Guid.NewGuid(), ClinicId = s.Clinic.Id, Question = question, ExpectedDocumentId = s.Doc.Id, ExpectedChunkId = s.Chunk1,
            CreatedAt = Now.AddMinutes(-minutesAgo), UpdatedAt = Now,
        };
        tweak?.Invoke(x);
        return x;
    }

    [PostgresFact]
    public async Task Case_counts_views_and_scopes()
    {
        var s = await SeedKnowledgeAsync();
        var generation = new KnowledgeRetrievalBenchmarkGeneration { Id = Guid.NewGuid(), ClinicId = s.Clinic.Id, CreatedAt = Now };
        await SeedAsync(generation);
        var g1 = Case(s, "generated 1", c => { c.GenerationId = generation.Id; c.SourceChunkHash = "h1"; }, 3);
        var g2 = Case(s, "generated reviewed", c => { c.IsReviewed = true; c.GenerationId = generation.Id; c.SourceChunkHash = "h1"; }, 2);
        var manual = Case(s, "manual", c => c.CaseType = BenchmarkCaseType.Manual, 1);
        var stale = Case(s, "stale", c => { c.IsStale = true; c.StaleReason = "gone"; c.SourceChunkHash = "h2"; }, 0);
        await SeedAsync(g1, g2, manual, stale);

        await using var db = NewContext();
        var repo = new KnowledgeBenchmarkRepository(db);

        Assert.Equal(new BenchmarkCaseCounts(4, 3, 1, 1, 1), await repo.GetCaseCountsAsync(s.Clinic.Id));
        Assert.Equal(new BenchmarkCaseCounts(0, 0, 0, 0, 0), await repo.GetCaseCountsAsync(Guid.NewGuid()));

        async Task<int> ViewTotal(string view, Guid? gen = null) => (await repo.ListCasesAsync(s.Clinic.Id, view, gen, 0, 50)).Total;
        Assert.Equal(4, await ViewTotal("all"));
        Assert.Equal(3, await ViewTotal("generated"));
        Assert.Equal(1, await ViewTotal("manual"));
        Assert.Equal(1, await ViewTotal("reviewed"));
        Assert.Equal(3, await ViewTotal("unreviewed"));
        Assert.Equal(1, await ViewTotal("stale"));
        Assert.Equal(2, await ViewTotal("all", generation.Id));

        var page = await repo.ListCasesAsync(s.Clinic.Id, "all", null, 1, 2);
        Assert.Equal(["manual", "generated reviewed"], page.Items.Select(i => i.Question));       // newest first

        Assert.Equal(2, (await repo.ListCasesInScopeAsync(s.Clinic.Id, BenchmarkCaseScope.Generation, generation.Id)).Count);
        Assert.Equal(1, (await repo.ListCasesInScopeAsync(s.Clinic.Id, BenchmarkCaseScope.Reviewed, null)).Count);
        Assert.Equal(3, (await repo.ListCasesInScopeAsync(s.Clinic.Id, BenchmarkCaseScope.Generated, null)).Count);
        Assert.Equal((4, 1), await repo.CountCasesInScopeAsync(s.Clinic.Id, BenchmarkCaseScope.All, null));
        Assert.Equal(4, (await repo.ListAllCasesAsync(s.Clinic.Id)).Count);

        Assert.Equivalent(new[] { "h1", "h2" }, await repo.ListCoveredSourceHashesAsync(s.Clinic.Id));
        Assert.Equal(generation.Id, Assert.Single(await repo.CountCasesByGenerationAsync(s.Clinic.Id, [generation.Id])).Key);
        Assert.Equal(2, (await repo.CountCasesByGenerationAsync(s.Clinic.Id, [generation.Id]))[generation.Id]);
        Assert.Equal(4, (await repo.ListQuestionsForChunksAsync(s.Clinic.Id, [s.Chunk1])).Count);
    }

    [PostgresFact]
    public async Task Cases_can_be_fetched_added_detached_and_deleted()
    {
        var s = await SeedKnowledgeAsync();
        var existing = Case(s, "existing");
        await SeedAsync(existing);

        await using var db = NewContext();
        var repo = new KnowledgeBenchmarkRepository(db);
        Assert.NotNull(await repo.GetCaseReadOnlyAsync(s.Clinic.Id, existing.Id));
        Assert.Null(await repo.GetCaseReadOnlyAsync(Guid.NewGuid(), existing.Id));
        Assert.NotNull(await repo.GetCaseAsync(s.Clinic.Id, existing.Id));

        var one = Case(s, "one"); var two = Case(s, "two"); var three = Case(s, "three");
        repo.AddCase(one);
        repo.AddCases([two, three]);
        repo.DetachCase(three);
        repo.DetachAllCases();                      // everything pending is forgotten
        Assert.Empty(db.ChangeTracker.Entries<KnowledgeRetrievalBenchmarkCase>());

        repo.AddCase(one);
        await db.SaveChangesAsync();
        Assert.Equal(2, (await repo.ListAllCasesAsync(s.Clinic.Id)).Count);

        Assert.Equal(0, await repo.DeleteCaseAsync(Guid.NewGuid(), one.Id));
        Assert.Equal(1, await repo.DeleteCaseAsync(s.Clinic.Id, one.Id));
        Assert.Single(await repo.ListAllCasesAsync(s.Clinic.Id));
    }

    [PostgresFact]
    public async Task Knowledge_base_views_only_expose_active_documents()
    {
        var s = await SeedKnowledgeAsync();
        var hidden = MakeDocument(s.Clinic.Id, "Hidden", active: false);
        var emptyDoc = MakeDocument(s.Clinic.Id, "Empty");
        await SeedAsync(hidden, emptyDoc);
        var hiddenChunk = await SeedChunkAsync(s.Clinic.Id, hidden.Id, 0, "hidden text");
        var other = MakeClinic();
        await SeedAsync(other);

        await using var db = NewContext();
        var repo = new KnowledgeBenchmarkRepository(db);

        var info = (await repo.GetActiveChunkAsync(s.Clinic.Id, s.Doc.Id, s.Chunk1))!;
        Assert.Equal("Pricing", info.DocumentTitle);
        Assert.Null(await repo.GetActiveChunkAsync(s.Clinic.Id, hidden.Id, hiddenChunk));
        Assert.Null(await repo.GetActiveChunkAsync(other.Id, s.Doc.Id, s.Chunk1));

        var docs = await repo.ListSourceDocumentsAsync(s.Clinic.Id);
        var listed = Assert.Single(docs);
        Assert.Equal((s.Doc.Id, 2), (listed.Id, listed.ChunkCount));

        Assert.Equal([s.Chunk1, s.Chunk2], (await repo.ListDocumentChunksAsync(s.Clinic.Id, s.Doc.Id)).Select(k => k.Id));
        Assert.Equal(2, (await repo.ListChunksAsync(s.Clinic.Id, [s.Chunk1, s.Chunk2, hiddenChunk])).Count - 1);   // inactive docs still resolve by id
        Assert.Equal(2, (await repo.ListActiveChunksAsync(s.Clinic.Id, null)).Count);
        Assert.Single(await repo.ListActiveChunksAsync(s.Clinic.Id, [s.Chunk2]));
        Assert.Equal(3, (await repo.ListDocumentStatesAsync(s.Clinic.Id)).Count);

        var stats = await repo.GetActiveChunkStatsAsync(s.Clinic.Id);
        Assert.Equal(2, stats.Count);
        Assert.True(stats.AverageChars > 60);
        Assert.Equal(0, (await repo.GetActiveChunkStatsAsync(other.Id)).Count);
        Assert.Null((await repo.GetActiveChunkStatsAsync(other.Id)).AverageChars);

        Assert.Contains("first chunk", await repo.GetChunkContentAsync(s.Clinic.Id, s.Chunk1, s.Doc.Id));
        Assert.Null(await repo.GetChunkContentAsync(other.Id, s.Chunk1, s.Doc.Id));
    }

    [PostgresFact]
    public async Task Sampling_skips_covered_short_and_noisy_chunks()
    {
        var s = await SeedKnowledgeAsync();
        var noisy = await SeedChunkAsync(s.Clinic.Id, s.Doc.Id, 2, new string('c', 80) + " 482 Views 0 Comments");
        var readMore = await SeedChunkAsync(s.Clinic.Id, s.Doc.Id, 3, new string('d', 80) + " read more");
        var tiny = await SeedChunkAsync(s.Clinic.Id, s.Doc.Id, 4, "tiny");
        await SeedAsync(Case(s, "covers chunk 1"));    // chunk 1 is already covered

        await using var db = NewContext();
        var sample = await new KnowledgeBenchmarkRepository(db).SampleUncoveredChunksAsync(s.Clinic.Id, minChars: 50, poolSize: 10);
        Assert.Equal([s.Chunk2], sample.Select(x => x.ChunkId));
        Assert.DoesNotContain(sample, x => x.ChunkId == noisy || x.ChunkId == readMore || x.ChunkId == tiny);
    }

    [PostgresFact]
    public async Task Runs_are_listed_found_and_checked_for_progress()
    {
        var s = await SeedKnowledgeAsync();
        KnowledgeRetrievalBenchmarkRun Run(string status, int minutesAgo) => new()
        {
            Id = Guid.NewGuid(), ClinicId = s.Clinic.Id, Status = status, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo),
        };
        var done = Run(BenchmarkRunStatus.Completed, 30); var running = Run(BenchmarkRunStatus.Running, 5); var failed = Run(BenchmarkRunStatus.Failed, 1);
        await SeedAsync(done, running, failed);

        await using var db = NewContext();
        var repo = new KnowledgeBenchmarkRepository(db);
        Assert.NotNull(await repo.GetRunAsync(done.Id));
        Assert.NotNull(await repo.GetRunReadOnlyAsync(s.Clinic.Id, done.Id));
        Assert.Null(await repo.GetRunReadOnlyAsync(Guid.NewGuid(), done.Id));
        Assert.True(await repo.RunExistsAsync(s.Clinic.Id, done.Id));
        Assert.False(await repo.RunExistsAsync(Guid.NewGuid(), done.Id));
        Assert.Equal(failed.Id, (await repo.GetLatestRunAsync(s.Clinic.Id, false))!.Id);
        Assert.Equal(done.Id, (await repo.GetLatestRunAsync(s.Clinic.Id, true))!.Id);
        Assert.Equal([failed.Id, running.Id], (await repo.ListRunsAsync(s.Clinic.Id, 2)).Select(r => r.Id));
        Assert.True(await repo.HasRunInProgressAsync(s.Clinic.Id, DateTimeOffset.UtcNow.AddHours(-1)));
        Assert.False(await repo.HasRunInProgressAsync(s.Clinic.Id, DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Contains(running.Id, (await repo.ListRunsInStatusAsync(BenchmarkRunStatus.Running)).Select(r => r.Id));
        Assert.Contains(running.Id, await repo.ListRunIdsInStatusAsync(BenchmarkRunStatus.Running));

        var added = Run(BenchmarkRunStatus.Pending, 0);
        repo.AddRun(added);
        await db.SaveChangesAsync();
        Assert.True(await new KnowledgeBenchmarkRepository(NewContext()).RunExistsAsync(s.Clinic.Id, added.Id));
    }

    [PostgresFact]
    public async Task Results_are_ordered_by_severity_and_filterable()
    {
        var s = await SeedKnowledgeAsync();
        var generation = new KnowledgeRetrievalBenchmarkGeneration { Id = Guid.NewGuid(), ClinicId = s.Clinic.Id, CreatedAt = Now };
        var run = new KnowledgeRetrievalBenchmarkRun { Id = Guid.NewGuid(), ClinicId = s.Clinic.Id, Status = BenchmarkRunStatus.Completed, CreatedAt = Now };
        var pending = new KnowledgeRetrievalBenchmarkRun { Id = Guid.NewGuid(), ClinicId = s.Clinic.Id, Status = BenchmarkRunStatus.Running, CreatedAt = Now };
        var theCase = Case(s, "q");
        await SeedAsync(generation, run, pending);
        await SeedAsync(theCase);

        KnowledgeRetrievalBenchmarkResult Result(KnowledgeRetrievalBenchmarkRun r, string classification, int minutes, int? rank = null) => new()
        {
            Id = Guid.NewGuid(), ClinicId = s.Clinic.Id, BenchmarkRunId = r.Id, BenchmarkCaseId = theCase.Id, Question = "q " + classification,
            ExpectedDocumentId = s.Doc.Id, ExpectedChunkId = s.Chunk1, GenerationId = generation.Id, ResultClassification = classification,
            ExpectedChunkRank = rank, ExpectedDocumentBestRank = rank, CreatedAt = Now.AddMinutes(-minutes),
        };
        var exact = Result(run, BenchmarkClassification.ExactChunkHit, 4, 1);
        var miss = Result(run, BenchmarkClassification.Miss, 3);
        var docOnly = Result(run, BenchmarkClassification.DocumentOnlyHit, 2, 4);
        var error = Result(run, BenchmarkClassification.Error, 1);
        var other = Result(pending, BenchmarkClassification.Miss, 0);

        await using (var db = NewContext())
        {
            var repo = new KnowledgeBenchmarkRepository(db);
            foreach (var r in new[] { exact, miss, docOnly, error, other }) repo.AddResult(r);
            var detached = Result(run, BenchmarkClassification.StaleCase, 0);
            repo.AddResult(detached);
            repo.DetachResult(detached);
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var q = new KnowledgeBenchmarkRepository(read);
        var (items, total) = await q.ListRunResultsAsync(s.Clinic.Id, run.Id, null, 0, 10);
        Assert.Equal(4, total);
        Assert.Equal([BenchmarkClassification.Miss, BenchmarkClassification.DocumentOnlyHit, BenchmarkClassification.Error, BenchmarkClassification.ExactChunkHit],
            items.Select(i => i.ResultClassification));
        Assert.Single((await q.ListRunResultsAsync(s.Clinic.Id, run.Id, BenchmarkClassification.Miss, 0, 10)).Items);
        Assert.Single((await q.ListRunResultsAsync(s.Clinic.Id, run.Id, null, 3, 10)).Items);

        Assert.Equal(miss.Id, (await q.GetResultAsync(s.Clinic.Id, run.Id, miss.Id))!.Id);
        Assert.Null(await q.GetResultAsync(s.Clinic.Id, pending.Id, miss.Id));
        Assert.Equal(other.Id, (await q.GetLatestResultForCaseAsync(s.Clinic.Id, theCase.Id))!.Id);
        Assert.Equal(5, (await q.ListResultsForCasesAsync(s.Clinic.Id, [theCase.Id])).Count);
        Assert.Equal(4, (await q.ListRunResultRanksAsync(s.Clinic.Id, run.Id)).Count);

        var completed = await q.ListCompletedResultRanksForGenerationsAsync(s.Clinic.Id, [generation.Id]);
        Assert.Equal(4, completed.Count);                                  // results of the still-running run are excluded
        Assert.All(completed, r => Assert.NotNull(r.RunAt));
    }

    [PostgresFact]
    public async Task Generation_lifecycle_pending_to_completed_cancelled_failed_or_expired()
    {
        var s = await SeedKnowledgeAsync();
        KnowledgeRetrievalBenchmarkGeneration Generation(string status = BenchmarkGenerationStatus.Pending, int minutesAgo = 0) => new()
        {
            Id = Guid.NewGuid(), ClinicId = s.Clinic.Id, Status = status, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo),
        };
        var toComplete = Generation(minutesAgo: 1); var toCancel = Generation(minutesAgo: 2); var toFail = Generation(minutesAgo: 3);
        var stuck = Generation(minutesAgo: 120); var done = Generation(BenchmarkGenerationStatus.Completed, 200);
        await SeedAsync(toComplete, toCancel, toFail, stuck, done);

        await using var db = NewContext();
        var repo = new KnowledgeBenchmarkRepository(db);
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(toComplete.Id, await repo.GetPendingGenerationIdAsync(s.Clinic.Id));
        Assert.Null(await repo.GetPendingGenerationIdAsync(Guid.NewGuid()));
        Assert.True(await repo.GenerationExistsAsync(s.Clinic.Id, done.Id));
        Assert.False(await repo.GenerationExistsAsync(Guid.NewGuid(), done.Id));
        Assert.NotNull(await repo.GetGenerationAsync(done.Id));
        Assert.NotNull(await repo.GetGenerationReadOnlyAsync(done.Id));
        Assert.NotNull(await repo.GetGenerationReadOnlyAsync(s.Clinic.Id, done.Id));
        Assert.Null(await repo.GetGenerationReadOnlyAsync(Guid.NewGuid(), done.Id));
        Assert.Equal(5, (await repo.ListGenerationsAsync(s.Clinic.Id, 10)).Count);
        Assert.Equal(2, (await repo.ListGenerationsAsync(s.Clinic.Id, 2)).Count);
        Assert.Equal(2, (await repo.GetGenerationRequestTimesAsync(s.Clinic.Id, [done.Id, stuck.Id, Guid.NewGuid()])).Count);

        Assert.Equal(1, await repo.ClaimPendingGenerationAsync(toComplete.Id, now));
        Assert.Equal(0, await repo.ClaimPendingGenerationAsync(toComplete.Id, now));            // only a pending one can be claimed
        Assert.Equal(0, await repo.CancelPendingGenerationAsync(Guid.NewGuid(), toCancel.Id, "stop", now));
        Assert.Equal(1, await repo.CancelPendingGenerationAsync(s.Clinic.Id, toCancel.Id, "stopped by user", now));
        await repo.FailPendingGenerationAsync(toFail.Id, "n8n error", now);
        await repo.ExpirePendingGenerationsAsync(s.Clinic.Id, now.AddMinutes(-60), "timed out", now);

        await using var check = NewContext();
        var verify = new KnowledgeBenchmarkRepository(check);
        Assert.Equal(BenchmarkGenerationStatus.Completed, (await verify.GetGenerationReadOnlyAsync(toComplete.Id))!.Status);
        var cancelled = (await verify.GetGenerationReadOnlyAsync(toCancel.Id))!;
        Assert.Equal((BenchmarkGenerationStatus.Cancelled, "stopped by user"), (cancelled.Status, cancelled.ErrorMessage));
        Assert.Equal(BenchmarkGenerationStatus.Failed, (await verify.GetGenerationReadOnlyAsync(toFail.Id))!.Status);
        var expired = (await verify.GetGenerationReadOnlyAsync(stuck.Id))!;
        Assert.Equal((BenchmarkGenerationStatus.Failed, "timed out"), (expired.Status, expired.ErrorMessage));

        var added = Generation();
        repo.AddGeneration(added);
        var ghost = Generation();
        repo.AddGeneration(ghost);
        repo.DetachGeneration(ghost);
        await db.SaveChangesAsync();
        Assert.True(await new KnowledgeBenchmarkRepository(NewContext()).GenerationExistsAsync(s.Clinic.Id, added.Id));
        Assert.False(await new KnowledgeBenchmarkRepository(NewContext()).GenerationExistsAsync(s.Clinic.Id, ghost.Id));
    }
}
