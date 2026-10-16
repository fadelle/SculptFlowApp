namespace Omni.App.Entities.Dtos.Caching;

/// <summary>Wraps a cached value that may legitimately be null or a value type ("no booking settings yet", "no active
/// membership"), since ICacheManager doesn't cache a null result.</summary>
public sealed record CachedValue<T>(T Value);
