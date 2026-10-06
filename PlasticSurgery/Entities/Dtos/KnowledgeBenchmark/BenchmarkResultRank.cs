namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

/// <summary>The ranks of one benchmark result, enough to re-score it (RunAt is set when the run was joined in).</summary>
public record BenchmarkResultRank(Guid? GenerationId, Guid RunId, DateTimeOffset? RunAt, string Classification, int? ChunkRank,
    int? DocumentRank);
