using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Integrations.Knowledge.Benchmark;

/// <summary>One chunk production search RETURNED for a question, in rank order (index 0 = rank 1). Chunks that
/// scored under the clinic's minimum similarity are never passed here — production search would not return them.</summary>
public record BenchmarkRetrievedItem(Guid DocumentId, Guid ChunkId);

/// <summary>The deterministic outcome of scoring one case against the ranked results. Ranks are 1-based and null when
/// not found; the *TopN flags are "found at rank &lt;= N".</summary>
public record BenchmarkScore(
    int? ExpectedChunkRank,
    int? ExpectedDocumentBestRank,
    bool ChunkTop1,
    bool ChunkTop3,
    bool ChunkTop5,
    bool DocumentTop1,
    bool DocumentTop3,
    bool DocumentTop5,
    double ChunkReciprocalRank,
    double DocumentReciprocalRank,
    string Classification);

/// <summary>Aggregate metrics over the SCORED cases of a run (stale and errored cases are never included). All
/// null when nothing was scored. Hit@K = share of scored cases whose expected chunk (or document) ranked &lt;= K;
/// MRR = mean of 1/rank, counting 0 when not found.</summary>
public record BenchmarkMetrics(
    int ScoredCases,
    double? ChunkTop1,
    double? ChunkTop3,
    double? ChunkTop5,
    double? ChunkMrr,
    double? DocumentTop1,
    double? DocumentTop3,
    double? DocumentTop5,
    double? DocumentMrr);

public interface IKnowledgeBenchmarkScorer
{
    /// <summary>Scores one case. STRICT chunk match: the returned item must have BOTH the expected chunk id and the
    /// expected document id. Document-level: the best-ranked returned item from the expected document.</summary>
    BenchmarkScore Score(Guid expectedDocumentId, Guid expectedChunkId, IReadOnlyList<BenchmarkRetrievedItem> ranked);

    /// <summary>The same scoring from ranks already known (e.g. stored on a result): null = not returned. Used to aggregate stored
    /// results per generation with exactly the same rules as a live run.</summary>
    BenchmarkScore ScoreFromRanks(int? chunkRank, int? documentRank);

    BenchmarkMetrics Summarize(IReadOnlyCollection<BenchmarkScore> scoredCases);
}

/// <summary>No LLM, no I/O: identical inputs always give identical scores.</summary>
public class KnowledgeBenchmarkScorer : IKnowledgeBenchmarkScorer
{
    public BenchmarkScore Score(Guid expectedDocumentId, Guid expectedChunkId, IReadOnlyList<BenchmarkRetrievedItem> ranked)
    {
        int? chunkRank = null;
        int? documentRank = null;

        for (var i = 0; i < ranked.Count; i++)
        {
            var rank = i + 1;
            var item = ranked[i];

            if (chunkRank is null && item.ChunkId == expectedChunkId && item.DocumentId == expectedDocumentId)
            {
                chunkRank = rank;
            }
            if (documentRank is null && item.DocumentId == expectedDocumentId)
            {
                documentRank = rank;
            }
        }

        return ScoreFromRanks(chunkRank, documentRank);
    }

    public BenchmarkScore ScoreFromRanks(int? chunkRank, int? documentRank)
    {
        // An exact chunk hit is always also a document hit (same document, so its rank can't be worse).
        var classification = chunkRank is not null ? BenchmarkClassification.ExactChunkHit
            : documentRank is not null ? BenchmarkClassification.DocumentOnlyHit
            : BenchmarkClassification.Miss;

        return new BenchmarkScore(
            chunkRank, documentRank,
            ChunkTop1: chunkRank is <= 1, ChunkTop3: chunkRank is <= 3, ChunkTop5: chunkRank is <= 5,
            DocumentTop1: documentRank is <= 1, DocumentTop3: documentRank is <= 3, DocumentTop5: documentRank is <= 5,
            ChunkReciprocalRank: chunkRank is { } c ? 1.0 / c : 0,
            DocumentReciprocalRank: documentRank is { } d ? 1.0 / d : 0,
            classification);
    }

    public BenchmarkMetrics Summarize(IReadOnlyCollection<BenchmarkScore> scoredCases)
    {
        var n = scoredCases.Count;
        if (n == 0) return new BenchmarkMetrics(0, null, null, null, null, null, null, null, null);

        double Rate(Func<BenchmarkScore, bool> pass) => scoredCases.Count(pass) / (double)n;
        double Mean(Func<BenchmarkScore, double> value) => scoredCases.Sum(value) / n;

        return new BenchmarkMetrics(
            n,
            Rate(s => s.ChunkTop1), Rate(s => s.ChunkTop3), Rate(s => s.ChunkTop5), Mean(s => s.ChunkReciprocalRank),
            Rate(s => s.DocumentTop1), Rate(s => s.DocumentTop3), Rate(s => s.DocumentTop5), Mean(s => s.DocumentReciprocalRank));
    }
}
