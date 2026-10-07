namespace PlasticSurgery.Entities.Responses.Knowledge;

/// <summary>All of a clinic's Knowledge Base retrieval/embedding settings, as persisted. The first four
/// are read-only (see KnowledgeSearchSettings); the rest are the tunable ones.</summary>
public record KnowledgeSettingsResponse(
    string EmbeddingModel,
    int VectorDimension,
    string SimilarityMethod,
    string VectorIndexType,
    int ChunkSizeTokens,
    int ChunkOverlapTokens,
    int TopK,
    double MinimumSimilarity,
    DateTimeOffset UpdatedAt
);
