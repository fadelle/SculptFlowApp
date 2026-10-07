using System.Text.Json;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Providers.Caching;
using PlasticSurgery.Entities.Dtos.Caching;

namespace PlasticSurgery.Business.Managers;

/// <summary>Singleton. JSON (de)serialization and failure handling on top of whichever ICacheAdapter is registered.</summary>
public class CacheManager : ICacheManager
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ICacheAdapter _adapter;
    private readonly ILogger<CacheManager> _logger;

    public CacheManager(ICacheAdapter adapter, ILogger<CacheManager> logger)
    {
        _adapter = adapter;
        _logger = logger;
    }

    public async Task<T?> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T?>> factory, CancellationToken ct = default)
        where T : class
    {
        try
        {
            var cached = await _adapter.GetAsync(key, ct);
            if (cached is not null) return JsonSerializer.Deserialize<T>(cached, Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache read of {Key} failed; loading it directly.", key);
        }

        var value = await factory(ct);
        if (value is null) return null;
        try
        {
            await _adapter.SetAsync(key, JsonSerializer.Serialize(value, Json), ttl, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Cache write of {Key} failed.", key);
        }
        return value;
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return await _adapter.RemoveAsync(key, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Cache remove of {Key} failed; it stays until it expires.", key);
            return false;
        }
    }

    public async Task<int> RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        try
        {
            return await _adapter.RemoveByPrefixAsync(prefix, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Cache remove of prefix {Prefix} failed; those keys stay until they expire.", prefix);
            return 0;
        }
    }

    public Task<IReadOnlyList<CacheEntryInfo>> ListAsync(string? prefix = null, CancellationToken ct = default) =>
        _adapter.ListAsync(prefix, ct);

    public async Task<(CacheEntryInfo Info, string Json)?> GetRawAsync(string key, CancellationToken ct = default)
    {
        var info = await _adapter.GetInfoAsync(key, ct);
        var json = info is null ? null : await _adapter.GetAsync(key, ct);
        return json is null ? null : (info!, json);
    }

    public Task<int> ClearAsync(CancellationToken ct = default) => _adapter.ClearAsync(ct);
}
