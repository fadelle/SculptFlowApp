namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>A client's knowledge search settings, with the Top K range the platform allows (Knowledge MinTopK/MaxTopK settings;
/// QA t6-admin-05: the portal's form said 1 to 20 while the main app takes 1 to 10).</summary>
public record SearchSettingsDetail(Guid Id, Guid ClientId, string EmbeddingModel, int VectorDimension, string VectorIndexType,
    int ChunkSizeTokens, int ChunkOverlapTokens, int TopK, double MinimumSimilarity, int MinTopK = 1, int MaxTopK = 10);
