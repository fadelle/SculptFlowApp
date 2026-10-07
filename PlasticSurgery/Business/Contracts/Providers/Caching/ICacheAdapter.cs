using PlasticSurgery.Entities.Dtos.Caching;

namespace PlasticSurgery.Business.Contracts.Providers.Caching;

/// <summary>
/// Where cached values are stored. Values are JSON strings so every adapter behaves the same: MemoryCacheAdapter today,
/// a Redis adapter later (register it instead and nothing else changes). Code uses ICacheManager, not this.
/// </summary>
public interface ICacheAdapter
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    Task<CacheEntryInfo?> GetInfoAsync(string key, CancellationToken ct = default);

    Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>True when the key existed.</summary>
    Task<bool> RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>Removes every key starting with <paramref name="prefix"/>; returns how many.</summary>
    Task<int> RemoveByPrefixAsync(string prefix, CancellationToken ct = default);

    /// <summary>Live keys, optionally only those starting with <paramref name="prefix"/>, ordered by key.</summary>
    Task<IReadOnlyList<CacheEntryInfo>> ListAsync(string? prefix = null, CancellationToken ct = default);

    /// <summary>Removes every key; returns how many.</summary>
    Task<int> ClearAsync(CancellationToken ct = default);
}
