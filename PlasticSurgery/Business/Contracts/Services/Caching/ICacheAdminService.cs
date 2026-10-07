using PlasticSurgery.Entities.Dtos.Caching;
using PlasticSurgery.Entities.Responses.Caching;

namespace PlasticSurgery.Business.Contracts.Services.Caching;

/// <summary>Platform-admin view of the cache: list keys, read one, remove one or a prefix, clear all.</summary>
public interface ICacheAdminService
{
    Task<IReadOnlyList<CacheEntryInfo>> ListAsync(string? prefix, CancellationToken ct);

    /// <summary>Throws KeyNotFoundException when the key isn't cached.</summary>
    Task<CacheEntryResponse> GetAsync(string key, CancellationToken ct);

    /// <summary>Throws KeyNotFoundException when the key isn't cached.</summary>
    Task RemoveAsync(string key, CancellationToken ct);

    Task<CacheClearResponse> RemoveByPrefixAsync(string prefix, CancellationToken ct);

    /// <summary>Removes every key and reloads the configuration settings from the database.</summary>
    Task<CacheClearResponse> ClearAsync(CancellationToken ct);
}
