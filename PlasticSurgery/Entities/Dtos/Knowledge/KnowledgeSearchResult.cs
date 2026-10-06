namespace PlasticSurgery.Entities.Dtos.Knowledge;

public record KnowledgeSearchResult(Guid DocumentId, string Title, string Category, string Content, double Score);
