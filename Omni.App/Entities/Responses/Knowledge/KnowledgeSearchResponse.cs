using Omni.App.Entities.Dtos.Knowledge;

namespace Omni.App.Entities.Responses.Knowledge;

public record KnowledgeSearchResponse(IReadOnlyList<KnowledgeSearchResult> Results);
