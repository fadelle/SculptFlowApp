using PlasticSurgery.Business.Contracts.Engines.KnowledgeBenchmark;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

namespace PlasticSurgery.Business.Engines.KnowledgeBenchmark;

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
