namespace PlasticSurgery.Entities.Dtos.Knowledge;

/// <summary>One chunk returned by the vector search, with its cosine similarity (1 = identical).</summary>
public record KnowledgeChunkMatch(Guid ChunkId, Guid DocumentId, string Title, string Category, string Content, double Score);
