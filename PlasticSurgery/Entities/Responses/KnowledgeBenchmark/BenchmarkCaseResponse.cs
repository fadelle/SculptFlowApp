using PlasticSurgery.Entities.Dtos.KnowledgeBenchmark;

namespace PlasticSurgery.Entities.Responses.KnowledgeBenchmark;

public record BenchmarkCaseResponse(
    Guid Id,
    string Question,
    string CaseType,
    bool IsReviewed,
    DateTimeOffset? ReviewedAt,
    /// <summary>The generation request that created this case (null for manual cases).</summary>
    Guid? GenerationId,
    bool IsStale,
    string? StaleReason,
    string? StaleExplanation,
    Guid ExpectedDocumentId,
    Guid ExpectedChunkId,
    string? ExpectedDocumentTitle,
    string? ExpectedChunkPreview,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    BenchmarkCaseLastResult? LastResult);
