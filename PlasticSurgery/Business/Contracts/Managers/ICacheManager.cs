using PlasticSurgery.Entities.Dtos.Caching;

namespace PlasticSurgery.Business.Contracts.Managers;

/// <summary>
/// The one way code caches things. Keys and lifetimes live in Common/Statics/CacheKeys. Values are stored as JSON
/// copies through ICacheAdapter, so cache plain DTOs/records, never tracked EF entities. A cache failure never fails
/// the caller: reads fall back to the factory, removes are logged.
/// </summary>
public interface ICacheManager
{
    /// <summary>Returns the cached value, or runs <paramref name="factory"/> and caches its result. Null is not cached.</summary>
    Task<T?> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T?>> factory, CancellationToken ct = default)
        where T : class;

    Task<bool> RemoveAsync(string key, CancellationToken ct = default);

    Task<int> RemoveByPrefixAsync(string prefix, CancellationToken ct = default);

    Task<IReadOnlyList<CacheEntryInfo>> ListAsync(string? prefix = null, CancellationToken ct = default);

    /// <summary>The stored JSON of one key, or null when it isn't cached.</summary>
    Task<(CacheEntryInfo Info, string Json)?> GetRawAsync(string key, CancellationToken ct = default);

    Task<int> ClearAsync(CancellationToken ct = default);
}
