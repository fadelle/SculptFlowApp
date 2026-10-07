using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

namespace PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

public record BenchmarkGenerationSummary(
    Guid Id,
    /// <summary>pending | completed | failed | cancelled (a pending generation with no callback after 30 minutes becomes failed;
    /// cancelled = stopped by staff).</summary>
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    int ChunksSent,
    int QuestionsReturned,
    int CasesCreated,
    int RejectedCount,
    /// <summary>How many of this generation's cases still exist (cases can be deleted).</summary>
    int CasesRemaining,
    string? ErrorMessage,
    /// <summary>Scores of this generation's cases in the most recent completed run that included them; null if never run.</summary>
    BenchmarkGenerationScores? LatestScores);
