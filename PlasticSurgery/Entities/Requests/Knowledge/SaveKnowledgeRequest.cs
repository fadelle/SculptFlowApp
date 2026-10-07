namespace PlasticSurgery.Entities.Requests.Knowledge;

/// <summary>Body for creating/updating a Knowledge Base document from the dashboard.</summary>
public record SaveKnowledgeRequest(string Title, string Category, string Content, bool IsActive = true);
