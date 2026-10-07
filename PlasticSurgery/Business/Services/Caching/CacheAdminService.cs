using System.Text.Json;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Caching;
using PlasticSurgery.Entities.Dtos.Caching;
using PlasticSurgery.Entities.Responses.Caching;

namespace PlasticSurgery.Business.Services.Caching;

public class CacheAdminService : ICacheAdminService
{
    private readonly ICacheManager _cache;
    private readonly IConfigManager _config;
    private readonly ILogger<CacheAdminService> _logger;

    public CacheAdminService(ICacheManager cache, IConfigManager config, ILogger<CacheAdminService> logger)
    {
        _cache = cache;
        _config = config;
        _logger = logger;
    }

    public Task<IReadOnlyList<CacheEntryInfo>> ListAsync(string? prefix, CancellationToken ct) => _cache.ListAsync(prefix, ct);

    public async Task<CacheEntryResponse> GetAsync(string key, CancellationToken ct)
    {
        var raw = await _cache.GetRawAsync(key, ct) ?? throw new KeyNotFoundException($"'{key}' isn't cached.");
        using var doc = JsonDocument.Parse(raw.Json);
        return new CacheEntryResponse(raw.Info.Key, raw.Info.ExpiresAt, raw.Info.SizeBytes, doc.RootElement.Clone());
    }

    public async Task RemoveAsync(string key, CancellationToken ct)
    {
        if (!await _cache.RemoveAsync(key, ct)) throw new KeyNotFoundException($"'{key}' isn't cached.");
        _logger.LogInformation("Cache: platform admin removed {Key}.", key);
    }

    public async Task<CacheClearResponse> RemoveByPrefixAsync(string prefix, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("A prefix is required; use clear to remove everything.");
        var removed = await _cache.RemoveByPrefixAsync(prefix, ct);
        _logger.LogInformation("Cache: platform admin removed {Count} keys under {Prefix}.", removed, prefix);
        return new CacheClearResponse(removed);
    }

    public async Task<CacheClearResponse> ClearAsync(CancellationToken ct)
    {
        var removed = await _cache.ClearAsync(ct);
        // Settings are read synchronously all over the app, so they stay in ConfigManager's own snapshot rather than the
        // cache; "clear all" reloads that snapshot too so it means "everything comes from the database again".
        await _config.RefreshAsync(ct);
        _logger.LogInformation("Cache: platform admin cleared {Count} keys and reloaded settings.", removed);
        return new CacheClearResponse(removed);
    }
}
