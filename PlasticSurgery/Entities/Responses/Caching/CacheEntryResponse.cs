using System.Text.Json;

namespace PlasticSurgery.Entities.Responses.Caching;

/// <summary>One cached key with its stored value (the cached object as JSON).</summary>
public record CacheEntryResponse(string Key, DateTimeOffset ExpiresAt, int SizeBytes, JsonElement Value);
