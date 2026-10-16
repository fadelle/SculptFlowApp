namespace Omni.App.Entities.Dtos.Caching;

/// <summary>One cache removal: an exact key, or every key starting with a prefix.</summary>
public sealed record CacheEviction(string Value, bool IsPrefix)
{
    public static CacheEviction Key(string key) => new(key, false);
    public static CacheEviction Prefix(string prefix) => new(prefix, true);
}
