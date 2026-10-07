using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using PlasticSurgery.Business.Contracts.Providers.Caching;
using PlasticSurgery.Entities.Dtos.Caching;

namespace PlasticSurgery.Business.Providers.Caching;

/// <summary>
/// Singleton. In-process cache on its own MemoryCache. MemoryCache can't list its keys, so the adapter keeps an index of
/// live keys (cleaned by the eviction callback) for listing, prefix removal and clear. Per instance: with more than one
/// app instance, a remove only reaches the instance that handled it (a Redis adapter fixes that).
/// </summary>
public sealed class MemoryCacheAdapter : ICacheAdapter, IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly ConcurrentDictionary<string, IndexEntry> _index = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public MemoryCacheAdapter(TimeProvider time)
    {
        _time = time;
    }

    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_cache.TryGetValue(key, out string? value) ? value : null);

    public Task<CacheEntryInfo?> GetInfoAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_index.TryGetValue(key, out var entry) && IsLive(entry) ? entry.Info : null);

    public Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct = default)
    {
        var entry = new IndexEntry(new CacheEntryInfo(key, _time.GetUtcNow().Add(ttl), Encoding.UTF8.GetByteCount(value)));
        var options = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };
        options.RegisterPostEvictionCallback(static (_, _, _, state) =>
        {
            // Eviction callbacks run later on the thread pool, possibly after the key was set again: only drop the
            // index entry if it is still this one (IndexEntry compares by reference).
            var (index, own) = ((ConcurrentDictionary<string, IndexEntry>, IndexEntry))state!;
            index.TryRemove(new KeyValuePair<string, IndexEntry>(own.Info.Key, own));
        }, (_index, entry));
        _index[key] = entry;
        _cache.Set(key, value, options);
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string key, CancellationToken ct = default)
    {
        var existed = _index.TryRemove(key, out var entry) && IsLive(entry);
        _cache.Remove(key);
        return Task.FromResult(existed);
    }

    public Task<int> RemoveByPrefixAsync(string prefix, CancellationToken ct = default) =>
        Task.FromResult(RemoveWhere(k => k.StartsWith(prefix, StringComparison.Ordinal)));

    public Task<IReadOnlyList<CacheEntryInfo>> ListAsync(string? prefix = null, CancellationToken ct = default)
    {
        IReadOnlyList<CacheEntryInfo> list = _index.Values
            .Where(IsLive)
            .Select(e => e.Info)
            .Where(i => string.IsNullOrEmpty(prefix) || i.Key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(i => i.Key, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(list);
    }

    public Task<int> ClearAsync(CancellationToken ct = default) => Task.FromResult(RemoveWhere(_ => true));

    public void Dispose() => _cache.Dispose();

    private int RemoveWhere(Func<string, bool> match)
    {
        var removed = 0;
        foreach (var key in _index.Keys.Where(match).ToList())
        {
            if (_index.TryRemove(key, out var entry) && IsLive(entry)) removed++;
            _cache.Remove(key);
        }
        return removed;
    }

    private bool IsLive(IndexEntry entry) => entry.Info.ExpiresAt > _time.GetUtcNow();

    private sealed class IndexEntry(CacheEntryInfo info)
    {
        public CacheEntryInfo Info { get; } = info;
    }
}
