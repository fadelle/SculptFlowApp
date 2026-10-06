using PlasticSurgery.Entities.Dtos.Knowledge;

namespace PlasticSurgery.Entities.Responses.Knowledge;

public record KnowledgeSearchResponse(IReadOnlyList<KnowledgeSearchResult> Results);
