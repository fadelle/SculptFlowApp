namespace PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

/// <summary>One row of "scores by generation" for a run. GenerationId null = cases with no generation (manual).</summary>
public record BenchmarkRunGenerationBreakdown(
    Guid? GenerationId,
    DateTimeOffset? GenerationRequestedAt,
    BenchmarkGenerationScores Scores);
