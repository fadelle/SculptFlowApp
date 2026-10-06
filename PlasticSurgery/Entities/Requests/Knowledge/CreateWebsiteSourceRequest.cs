namespace PlasticSurgery.Entities.Requests.Knowledge;

/// <summary>Body for POST /api/knowledge/websites. The clinic comes from the logged-in user, never from here.</summary>
public record CreateWebsiteSourceRequest(string? Url, string? CrawlMode, string? Category, bool IsActive = true);
