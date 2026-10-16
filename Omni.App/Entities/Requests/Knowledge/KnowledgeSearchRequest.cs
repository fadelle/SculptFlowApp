namespace Omni.App.Entities.Requests.Knowledge;

/// <summary>Body for POST /api/ai/knowledge/search. ClientId is the one the n8n workflow already has
/// in context (the AI agent supplies only Query) — nothing else about the conversation is passed.</summary>
public record KnowledgeSearchRequest(Guid ClientId, string Query, int? Limit = null);
