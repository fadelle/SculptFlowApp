namespace Omni.App.Entities.Dtos.Knowledge;

/// <summary>Limits a Knowledge Base search to some sources (an AI bot with knowledge_scope "selected"): these documents, plus
/// every page of these website sources. Empty on both sides = nothing matches.</summary>
public record KnowledgeSourceFilter(IReadOnlyCollection<Guid> DocumentIds, IReadOnlyCollection<Guid> WebsiteSourceIds);
