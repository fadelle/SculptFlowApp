using System.Text.Json;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

namespace PlasticSurgery.Business.Services.Knowledge;

// Async question generation (the "Generate Test Cases" flow). See the class comment on KnowledgeBenchmarkService.
public partial class KnowledgeBenchmarkService
{
    /// <summary>A pending generation with no callback after this long is marked failed (and its late callback refused).</summary>
    private static readonly TimeSpan GenerationTimeout = TimeSpan.FromMinutes(30);
    private const int MaxRawResponseChars = 100_000;
    private const int MaxRejectedStored = 100;
    private const int MaxQuestionsPerChunk = 3;

    private sealed record SentChunkRow(Guid DocumentId, Guid ChunkId, string DocumentTitle);

    public async Task<StartBenchmarkGenerationResponse> StartGenerationAsync(Guid clinicId, CancellationToken ct = default)
    {
        if (!_generator.IsConfigured)
        {
            throw new BenchmarkGeneratorNotConfiguredException(
                "The benchmark question generator isn't configured — set N8n:KnowledgeBenchmarkWebhookUrl (env var N8n__KnowledgeBenchmarkWebhookUrl).");
        }

        await ExpireOldGenerationsAsync(clinicId, ct);
        if (await _benchmark.GetPendingGenerationIdAsync(clinicId, ct) is not null)
        {
            throw new BenchmarkGenerationInProgressException();
        }

        await RefreshStaleAsync(clinicId, ct);
        var sample = await SampleSourceChunksAsync(clinicId, ct);
        if (sample.Count == 0)
        {
            return new StartBenchmarkGenerationResponse(null, "none", 0, 0, 0, Array.Empty<RejectedGeneratedQuestion>(),
                "No active chunks are available to generate from — either the Knowledge Base is empty or every chunk already has a benchmark case.");
        }

        // The row is committed BEFORE anything is sent, so even an instant callback from n8n finds it. It records exactly which
        // chunks were sent: that set — not anything n8n says — is what the returned ids are validated against.
        var generation = new KnowledgeRetrievalBenchmarkGeneration
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            Status = BenchmarkGenerationStatus.Pending,
            ChunksSent = sample.Count,
            SentChunksJson = JsonSerializer.Serialize(
                sample.Select(s => new SentChunkRow(s.DocumentId, s.ChunkId, Truncate(s.Title, 200))).ToList(), JsonOptions),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _benchmark.AddGeneration(generation);
        await _unitOfWork.SaveChangesAsync(ct);
        _benchmark.DetachGeneration(generation);

        GeneratorSendResult sent;
        try
        {
            sent = await _generator.SendAsync(clinicId, generation.Id,
                sample.Select(s => new BenchmarkSourceChunk(s.DocumentId, s.ChunkId, s.Title, s.Content)).ToList(), ct);
        }
        catch (BenchmarkGenerationException ex)
        {
            await FailGenerationAsync(generation.Id, ex.Message);
            throw;
        }

        if (sent.Immediate is null)
        {
            // The normal case: n8n acknowledged and will call back with the questions.
            return new StartBenchmarkGenerationResponse(generation.Id, BenchmarkGenerationStatus.Pending, sample.Count, 0, 0,
                Array.Empty<RejectedGeneratedQuestion>(), null);
        }

        // A workflow that answers synchronously: process the reply now, exactly as if it had been a callback.
        var result = await ReceiveGenerationResultAsync(generation.Id, sent.Immediate, sent.RawBody, requireEcho: true, ct);
        var summary = result.Summary;
        return new StartBenchmarkGenerationResponse(
            generation.Id, summary?.Status ?? BenchmarkGenerationStatus.Completed, sample.Count,
            summary?.QuestionsReturned ?? 0, summary?.CasesCreated ?? 0, result.Rejected ?? Array.Empty<RejectedGeneratedQuestion>(), null);
    }

    public async Task<GenerationReceiveResult> ReceiveGenerationResultAsync(
        Guid generationId, GeneratorResponse reply, string? rawBody, bool requireEcho, CancellationToken ct = default)
    {
        var generation = await _benchmark.GetGenerationReadOnlyAsync(generationId, ct);
        if (generation is null)
        {
            return new GenerationReceiveResult(GenerationReceiveStatus.NotFound, null, "No generation has this id.");
        }
        if (generation.Status == BenchmarkGenerationStatus.Completed)
        {
            return new GenerationReceiveResult(GenerationReceiveStatus.AlreadyProcessed, await SummaryOfAsync(generation, ct), null);
        }
        if (generation.Status is BenchmarkGenerationStatus.Failed or BenchmarkGenerationStatus.Cancelled)
        {
            return new GenerationReceiveResult(GenerationReceiveStatus.NotAccepting, await SummaryOfAsync(generation, ct),
                generation.ErrorMessage ?? "This generation was stopped or failed and no longer accepts questions.");
        }
        if (generation.CreatedAt < DateTimeOffset.UtcNow - GenerationTimeout)
        {
            await ExpireOldGenerationsAsync(generation.ClinicId, ct);
            var expired = await _benchmark.GetGenerationReadOnlyAsync(generationId, ct) ?? generation;
            return new GenerationReceiveResult(GenerationReceiveStatus.NotAccepting, await SummaryOfAsync(expired, ct), expired.ErrorMessage);
        }

        // Correlation check: the reply must belong to THIS request. A synchronous reply has nowhere else to say which request it
        // answers, so the echo is mandatory there; a callback already names the generation in its URL, so a body id is optional
        // but must agree with it.
        var echoed = Guid.TryParse(reply.GenerationId, out var parsed) ? parsed : (Guid?)null;
        if (requireEcho && echoed is null)
        {
            const string missing = "The question generator did not return the generationId it was sent. The n8n workflow must echo generationId back in its response.";
            await FailGenerationAsync(generationId, missing);
            throw new BenchmarkGenerationException(missing);
        }
        if (requireEcho && echoed != generationId)
        {
            const string wrong = "The question generator returned a different generationId than the one it was sent, so its response was discarded.";
            await FailGenerationAsync(generationId, wrong);
            throw new BenchmarkGenerationException(wrong);
        }
        if (!requireEcho && reply.GenerationId is not null && echoed != generationId)
        {
            throw new ArgumentException("The generationId in the body doesn't match the generationId in the URL.");
        }

        return await ApplyGenerationResultAsync(generationId, reply.Questions, rawBody, ct);
    }

    /// <summary>Validates the questions against the generation's STORED sent-chunk set and the live clinic Knowledge Base, then
    /// stores the valid ones as cases and completes the generation — all in one transaction. The generation is claimed first
    /// (pending → completed) so two simultaneous deliveries can never both create cases.</summary>
    private async Task<GenerationReceiveResult> ApplyGenerationResultAsync(
        Guid generationId, IReadOnlyList<RawGeneratedQuestion> raw, string? rawBody, CancellationToken ct)
    {
        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var claimed = await _benchmark.ClaimPendingGenerationAsync(generationId, now, ct);
        if (claimed == 0)
        {
            await tx.RollbackAsync(ct);
            var current = await _benchmark.GetGenerationReadOnlyAsync(generationId, ct)
                          ?? throw new InvalidOperationException("Benchmark generation not found.");
            return new GenerationReceiveResult(
                current.Status == BenchmarkGenerationStatus.Completed ? GenerationReceiveStatus.AlreadyProcessed : GenerationReceiveStatus.NotAccepting,
                await SummaryOfAsync(current, ct), current.ErrorMessage);
        }

        var generation = await _benchmark.GetGenerationAsync(generationId, ct)
                         ?? throw new InvalidOperationException("Benchmark generation not found.");
        var clinicId = generation.ClinicId; // from the stored row, never from the caller

        var sentRows = JsonSerializer.Deserialize<List<SentChunkRow>>(generation.SentChunksJson ?? "[]", JsonOptions) ?? new();
        var sent = sentRows.GroupBy(s => s.ChunkId).ToDictionary(g => g.Key, g => g.First());

        var rejected = new List<RejectedGeneratedQuestion>();
        var candidates = new List<(SentChunkRow Source, string Question)>();
        var seen = new HashSet<(Guid Chunk, string Question)>();
        var perChunk = new Dictionary<Guid, int>();
        var itemCap = Math.Max(generation.ChunksSent * MaxQuestionsPerChunk, 20);

        for (var i = 0; i < raw.Count; i++)
        {
            var item = raw[i];
            var question = NormalizeQuestion(item.Question);
            var label = question.Length > 60 ? question[..60] + "…" : question;

            if (i >= itemCap)
            {
                rejected.Add(new RejectedGeneratedQuestion(label, $"more questions than allowed for this generation (limit {itemCap})"));
                continue;
            }
            if (!Guid.TryParse(item.DocumentId, out var documentId) || !Guid.TryParse(item.ChunkId, out var chunkId))
            {
                rejected.Add(new RejectedGeneratedQuestion(label, "documentId/chunkId missing or not valid ids"));
                continue;
            }
            if (!sent.TryGetValue(chunkId, out var source))
            {
                rejected.Add(new RejectedGeneratedQuestion(label, "chunk was not part of this request"));
                continue;
            }
            if (source.DocumentId != documentId)
            {
                rejected.Add(new RejectedGeneratedQuestion(label, "chunk does not belong to the returned document"));
                continue;
            }
            if (question.Length == 0)
            {
                rejected.Add(new RejectedGeneratedQuestion(null, "empty question"));
                continue;
            }
            if (question.Length > MaxQuestionChars)
            {
                rejected.Add(new RejectedGeneratedQuestion(label, $"question longer than {MaxQuestionChars} characters"));
                continue;
            }
            if (!seen.Add((chunkId, question.ToLowerInvariant())))
            {
                rejected.Add(new RejectedGeneratedQuestion(label, "duplicate question for the same chunk"));
                continue;
            }
            perChunk[chunkId] = perChunk.GetValueOrDefault(chunkId) + 1;
            if (perChunk[chunkId] > MaxQuestionsPerChunk)
            {
                rejected.Add(new RejectedGeneratedQuestion(label, $"more than {MaxQuestionsPerChunk} questions for one chunk"));
                continue;
            }
            candidates.Add((source, question));
        }

        // Re-validate against the database (clinic-scoped) — the chunk may have been rebuilt or deleted while n8n was generating.
        var candidateChunkIds = candidates.Select(c => c.Source.ChunkId).Distinct().ToList();
        var live = (await _benchmark.ListActiveChunksAsync(clinicId, candidateChunkIds, ct)).ToDictionary(k => k.ChunkId);

        var existing = (await _benchmark.ListQuestionsForChunksAsync(clinicId, candidateChunkIds, ct))
            .Select(c => (c.ChunkId, c.Question.ToLowerInvariant())).ToHashSet();

        var toAdd = new List<KnowledgeRetrievalBenchmarkCase>();
        foreach (var (source, question) in candidates)
        {
            if (!live.TryGetValue(source.ChunkId, out var chunk) || chunk.DocumentId != source.DocumentId)
            {
                rejected.Add(new RejectedGeneratedQuestion(question, "chunk no longer exists for this clinic"));
                continue;
            }
            if (existing.Contains((source.ChunkId, question.ToLowerInvariant())))
            {
                rejected.Add(new RejectedGeneratedQuestion(question, "this question already exists for the chunk"));
                continue;
            }

            toAdd.Add(new KnowledgeRetrievalBenchmarkCase
            {
                Id = Guid.NewGuid(),
                ClinicId = clinicId,
                Question = question,
                ExpectedDocumentId = source.DocumentId,
                ExpectedChunkId = source.ChunkId,
                CaseType = BenchmarkCaseType.Generated,
                IsReviewed = false,
                GenerationId = generationId,
                SourceChunkHash = Sha256Hex(chunk.Content),
                SourceChunkPreview = Preview(chunk.Content, PreviewChars),
                SourceDocumentTitle = Truncate(source.DocumentTitle, 200),
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        _benchmark.AddCases(toAdd);
        generation.QuestionsReturned = raw.Count;
        generation.CasesCreated = toAdd.Count;
        generation.RejectedCount = rejected.Count;
        generation.RejectedJson = JsonSerializer.Serialize(rejected.Take(MaxRejectedStored).ToList(), JsonOptions);
        generation.RawResponse = rawBody is null ? null : Truncate(rawBody, MaxRawResponseChars);
        generation.ErrorMessage = null;
        await _unitOfWork.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new GenerationReceiveResult(GenerationReceiveStatus.Completed, await SummaryOfAsync(generation, ct), null, rejected);
    }

    public async Task<GenerationCancelResult> CancelGenerationAsync(Guid clinicId, Guid generationId, CancellationToken ct = default)
    {
        var exists = await _benchmark.GenerationExistsAsync(clinicId, generationId, ct);
        if (!exists) return new GenerationCancelResult(GenerationCancelStatus.NotFound, null);

        // Only a still-pending generation can be stopped. The status flip is a single conditional UPDATE, so it races safely with
        // n8n's callback: whichever claims the row first wins (a callback that already completed it makes this a no-op).
        var now = DateTimeOffset.UtcNow;
        var message = "Stopped by staff before n8n sent the questions. Any reply n8n sends later is ignored.";
        var stopped = await _benchmark.CancelPendingGenerationAsync(clinicId, generationId, message, now, ct);

        var current = await _benchmark.GetGenerationReadOnlyAsync(clinicId, generationId, ct)
                      ?? throw new InvalidOperationException("Benchmark generation not found.");
        return new GenerationCancelResult(
            stopped > 0 ? GenerationCancelStatus.Cancelled : GenerationCancelStatus.NotPending, await SummaryOfAsync(current, ct));
    }

    private async Task FailGenerationAsync(Guid generationId, string message)
    {
        // Only a still-pending generation can fail: never overwrite one that already completed.
        try
        {
            var now = DateTimeOffset.UtcNow;
            var error = Truncate(message, 1000);
            await _benchmark.FailPendingGenerationAsync(generationId, error, now, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record the failure of benchmark generation {GenerationId}.", generationId);
        }
    }

    /// <summary>Marks this clinic's generations that have waited longer than <see cref="GenerationTimeout"/> as failed, so a
    /// workflow that never calls back can't block "Generate Test Cases" forever.</summary>
    private async Task ExpireOldGenerationsAsync(Guid clinicId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - GenerationTimeout;
        var timeoutMessage = $"n8n did not send the questions within {(int)GenerationTimeout.TotalMinutes} minutes. Check the workflow's execution log, then generate again.";
        await _benchmark.ExpirePendingGenerationsAsync(clinicId, cutoff, timeoutMessage, now, ct);
    }

    // ---------------------------------------------------------------------------------------------
    // Reading generations (+ scores per generation)
    // ---------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<BenchmarkGenerationSummary>> ListGenerationsAsync(Guid clinicId, int take, CancellationToken ct = default)
    {
        await ExpireOldGenerationsAsync(clinicId, ct);
        var rows = await _benchmark.ListGenerationsAsync(clinicId, Math.Clamp(take, 1, 100), ct);
        return await SummariesOfAsync(clinicId, rows, ct);
    }

    public async Task<BenchmarkGenerationDetail?> GetGenerationAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        await ExpireOldGenerationsAsync(clinicId, ct);
        var g = await _benchmark.GetGenerationReadOnlyAsync(clinicId, id, ct);
        if (g is null) return null;

        var summary = (await SummariesOfAsync(clinicId, new[] { g }, ct))[0];
        var rejected = string.IsNullOrEmpty(g.RejectedJson) ? new() : JsonSerializer.Deserialize<List<RejectedGeneratedQuestion>>(g.RejectedJson, JsonOptions) ?? new();
        var sentChunks = (string.IsNullOrEmpty(g.SentChunksJson) ? new() : JsonSerializer.Deserialize<List<SentChunkRow>>(g.SentChunksJson, JsonOptions) ?? new())
            .Select(s => new BenchmarkGenerationChunk(s.DocumentId, s.ChunkId, s.DocumentTitle)).ToList();
        return new BenchmarkGenerationDetail(summary, rejected, sentChunks, g.RawResponse);
    }

    public async Task<IReadOnlyList<BenchmarkRunGenerationBreakdown>?> GetRunGenerationBreakdownAsync(Guid clinicId, Guid runId, CancellationToken ct = default)
    {
        var run = await _benchmark.GetRunReadOnlyAsync(clinicId, runId, ct);
        if (run is null) return null;

        var rows = await _benchmark.ListRunResultRanksAsync(clinicId, runId, ct);

        var generationIds = rows.Where(r => r.GenerationId != null).Select(r => r.GenerationId!.Value).Distinct().ToList();
        var requestedAt = generationIds.Count == 0
            ? new Dictionary<Guid, DateTimeOffset>()
            : await _benchmark.GetGenerationRequestTimesAsync(clinicId, generationIds, ct);

        return rows.GroupBy(r => r.GenerationId)
            .Select(g => new BenchmarkRunGenerationBreakdown(
                g.Key,
                g.Key is { } id && requestedAt.TryGetValue(id, out var at) ? at : (DateTimeOffset?)null,
                ScoresOf(g, run.Id, run.CreatedAt)))
            .OrderBy(b => b.GenerationId is null ? 1 : 0)             // generations first, "no generation" (manual) last
            .ThenByDescending(b => b.GenerationRequestedAt)
            .ToList();
    }

    /// <summary>Metrics over the SCORED results only (stale and errored ones never count), using the same scorer as a live run.</summary>
    private BenchmarkGenerationScores ScoresOf(IEnumerable<BenchmarkResultRank> results, Guid? runId, DateTimeOffset? runAt)
    {
        var scored = results
            .Where(r => r.Classification is not (BenchmarkClassification.StaleCase or BenchmarkClassification.Error))
            .Select(r => _scorer.ScoreFromRanks(r.ChunkRank, r.DocumentRank))
            .ToList();
        var m = _scorer.Summarize(scored);
        return new BenchmarkGenerationScores(runId, runAt, m.ScoredCases, m.ChunkTop1, m.ChunkTop3, m.ChunkTop5, m.ChunkMrr,
            m.DocumentTop1, m.DocumentTop3, m.DocumentTop5, m.DocumentMrr);
    }

    private async Task<BenchmarkGenerationSummary> SummaryOfAsync(KnowledgeRetrievalBenchmarkGeneration g, CancellationToken ct) =>
        (await SummariesOfAsync(g.ClinicId, new[] { g }, ct))[0];

    private async Task<List<BenchmarkGenerationSummary>> SummariesOfAsync(
        Guid clinicId, IReadOnlyCollection<KnowledgeRetrievalBenchmarkGeneration> generations, CancellationToken ct)
    {
        if (generations.Count == 0) return new();
        var ids = generations.Select(g => g.Id).ToList();
        var remainingById = await _benchmark.CountCasesByGenerationAsync(clinicId, ids, ct);

        // "Latest scores" = this generation's cases in the most recent COMPLETED run that included any of them.
        var resultRows = await _benchmark.ListCompletedResultRanksForGenerationsAsync(clinicId, ids, ct);

        var latestScores = new Dictionary<Guid, BenchmarkGenerationScores>();
        foreach (var byGeneration in resultRows.Where(r => r.GenerationId != null).GroupBy(r => r.GenerationId!.Value))
        {
            var latest = byGeneration.OrderByDescending(r => r.RunAt).First();
            var inLatestRun = byGeneration.Where(r => r.RunId == latest.RunId);
            latestScores[byGeneration.Key] = ScoresOf(inLatestRun, latest.RunId, latest.RunAt);
        }

        return generations.Select(g => new BenchmarkGenerationSummary(
            g.Id, g.Status, g.CreatedAt, g.CompletedAt, g.ChunksSent, g.QuestionsReturned, g.CasesCreated, g.RejectedCount,
            remainingById.GetValueOrDefault(g.Id), g.ErrorMessage, latestScores.GetValueOrDefault(g.Id))).ToList();
    }
}
