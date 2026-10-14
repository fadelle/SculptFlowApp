using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Caching;

namespace PlasticSurgery.Persistence.Helpers;

/// <summary>Read-through for repositories whose cache is optional (tests and tools build them without one).</summary>
internal static class CacheReads
{
    public static Task<T?> ReadThroughAsync<T>(this ICacheManager? cache, string key, CacheArea area,
        Func<CancellationToken, Task<T?>> load, CancellationToken ct) where T : class =>
        cache is null ? load(ct) : cache.GetOrCreateAsync(key, cache.LifetimeOf(area), load, ct);

    /// <summary>Like <see cref="ReadThroughAsync{T}"/> for a value that may be null (cached as "none" too).</summary>
    public static async Task<T> ReadThroughValueAsync<T>(this ICacheManager? cache, string key, CacheArea area,
        Func<CancellationToken, Task<T>> load, CancellationToken ct)
    {
        if (cache is null) return await load(ct);
        var box = await cache.GetOrCreateAsync(key, cache.LifetimeOf(area), async token => new CachedValue<T>(await load(token)), ct);
        return box!.Value;
    }

    public static Task RemoveIfCachedAsync(this ICacheManager? cache, IEnumerable<CacheEviction> evictions, CancellationToken ct) =>
        cache is null ? Task.CompletedTask : cache.RemoveAsync(evictions, ct);
}
