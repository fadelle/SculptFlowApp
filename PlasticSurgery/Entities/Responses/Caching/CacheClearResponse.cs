namespace PlasticSurgery.Entities.Responses.Caching;

/// <summary>How many keys a remove-by-prefix or clear-all call removed.</summary>
public record CacheClearResponse(int Removed);
