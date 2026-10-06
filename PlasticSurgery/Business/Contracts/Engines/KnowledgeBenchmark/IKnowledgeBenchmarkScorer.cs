using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

namespace PlasticSurgery.Business.Contracts.Engines.KnowledgeBenchmark;

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
