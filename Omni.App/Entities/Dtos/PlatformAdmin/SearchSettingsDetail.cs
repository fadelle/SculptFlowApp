namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>A client's knowledge search settings.</summary>
public record SearchSettingsDetail(Guid Id, Guid ClientId, string EmbeddingModel, int VectorDimension, string VectorIndexType,
    int ChunkSizeTokens, int ChunkOverlapTokens, int TopK, double MinimumSimilarity);
