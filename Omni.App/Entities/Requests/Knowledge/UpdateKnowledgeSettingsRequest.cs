namespace Omni.App.Entities.Requests.Knowledge;

/// <summary>The ONLY settings staff can change. Deliberately has no embedding-model / dimension /
/// similarity / index fields — those can't be changed from the client UI or API.</summary>
public record UpdateKnowledgeSettingsRequest(int ChunkSizeTokens, int ChunkOverlapTokens, int TopK, double MinimumSimilarity);
