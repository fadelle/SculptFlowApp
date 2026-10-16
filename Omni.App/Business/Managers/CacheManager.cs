using System.Text.Json;
using System.Text.Json.Serialization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Providers.Caching;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Caching;

namespace Omni.App.Business.Managers;

/// <summary>Singleton. JSON (de)serialization and failure handling on top of whichever ICacheAdapter is registered.</summary>
public class CacheManager : ICacheManager
{
    // IgnoreCycles: read-only entity copies can hold a loaded navigation that points back (connection ↔ its calendars).
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { ReferenceHandler = ReferenceHandler.IgnoreCycles };

    private readonly ICacheAdapter _adapter;
    private readonly ILogger<CacheManager> _logger;
    private readonly IConfigManager? _config;

    // config is optional so tests and tools can build it alone; without it the declared defaults apply.
    public CacheManager(ICacheAdapter adapter, ILogger<CacheManager> logger, IConfigManager? config = null)
    {
        _adapter = adapter;
        _logger = logger;
        _config = config;
    }

    public TimeSpan LifetimeOf(CacheArea area) => TimeSpan.FromMinutes(area switch
    {
        CacheArea.Clients => _config?.CacheClientsMinutes ?? Default(ConfigDefaults.CacheClientsMinutes),
        CacheArea.StaffLookup => _config?.CacheStaffLookupMinutes ?? Default(ConfigDefaults.CacheStaffLookupMinutes),
        CacheArea.Services => _config?.CacheServicesMinutes ?? Default(ConfigDefaults.CacheServicesMinutes),
        CacheArea.Scheduling => _config?.CacheSchedulingMinutes ?? Default(ConfigDefaults.CacheSchedulingMinutes),
        CacheArea.Channels => _config?.CacheChannelsMinutes ?? Default(ConfigDefaults.CacheChannelsMinutes),
        CacheArea.BillingRates => _config?.CacheBillingRatesMinutes ?? Default(ConfigDefaults.CacheBillingRatesMinutes),
        CacheArea.BusinessTypes => _config?.CacheBusinessTypesMinutes ?? Default(ConfigDefaults.CacheBusinessTypesMinutes),
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, null),
    });

    private static int Default(Entities.Dtos.Configuration.ConfigDefinition setting) => int.Parse(setting.DefaultValue);

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

    public async Task RemoveAsync(IEnumerable<CacheEviction> evictions, CancellationToken ct = default)
    {
        foreach (var eviction in evictions.Distinct())
        {
            if (eviction.IsPrefix) await RemoveByPrefixAsync(eviction.Value, ct);
            else await RemoveAsync(eviction.Value, ct);
        }
    }

    public Task<int> ClearAsync(CancellationToken ct = default) => _adapter.ClearAsync(ct);
}
