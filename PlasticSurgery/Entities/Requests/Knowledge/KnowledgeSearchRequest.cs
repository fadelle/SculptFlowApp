namespace PlasticSurgery.Entities.Requests.Knowledge;

/// <summary>Body for POST /api/ai/knowledge/search. ClinicId is the one the n8n workflow already has
/// in context (the AI agent supplies only Query) — nothing else about the conversation is passed.</summary>
public record KnowledgeSearchRequest(Guid ClinicId, string Query, int? Limit = null);
