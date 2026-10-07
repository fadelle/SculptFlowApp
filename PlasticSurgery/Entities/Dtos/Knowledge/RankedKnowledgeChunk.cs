namespace PlasticSurgery.Entities.Dtos.Knowledge;

/// <summary>One top-K candidate from the similarity search. <see cref="Score"/> is the raw cosine similarity;
/// <see cref="MeetsMinimumSimilarity"/> says whether production search would actually return it.</summary>
public record RankedKnowledgeChunk(
    Guid ChunkId,
    Guid DocumentId,
    string Title,
    string Category,
    string Content,
    double Score,
    bool MeetsMinimumSimilarity);
