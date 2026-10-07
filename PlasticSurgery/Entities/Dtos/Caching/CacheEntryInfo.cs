namespace PlasticSurgery.Entities.Dtos.Caching;

/// <summary>One cached key as the cache adapter reports it: when it expires and how big its stored JSON is.</summary>
public record CacheEntryInfo(string Key, DateTimeOffset ExpiresAt, int SizeBytes);
