namespace PlasticSurgery.Entities.Dtos.Knowledge;

/// <summary>A document produced by an external ingestion subsystem (the website crawler). The Knowledge Base
/// treats it like any other: same chunking, same embeddings, same search.</summary>
public record ExternalKnowledgeDocument(string SourceType, string Title, string Category, string Content, bool IsActive, string? SourceUrl);
