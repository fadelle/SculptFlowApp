using Microsoft.Extensions.Logging.Abstractions;
using PlasticSurgery.Business.Managers;
using PlasticSurgery.Business.Providers.Caching;

namespace PlasticSurgery.Tests;

/// <summary>MemoryCacheAdapter (key index, prefix removal, expiry) and CacheManager (JSON copies, null not cached).</summary>
public class CacheTests
{
    private sealed record Item(string Name, int Count);

    private static (CacheManager Cache, MemoryCacheAdapter Adapter, ManualTimeProvider Time) Create()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        var adapter = new MemoryCacheAdapter(time);
        return (new CacheManager(adapter, NullLogger<CacheManager>.Instance), adapter, time);
    }

    [Fact]
    public async Task GetOrCreate_runs_the_factory_once_and_returns_copies()
    {
        var (cache, _, _) = Create();
        var calls = 0;
        Task<Item?> Factory(CancellationToken _) { calls++; return Task.FromResult<Item?>(new Item("a", 1)); }

        var first = await cache.GetOrCreateAsync("t:a", TimeSpan.FromMinutes(5), Factory);
        var second = await cache.GetOrCreateAsync("t:a", TimeSpan.FromMinutes(5), Factory);

        Assert.Equal(1, calls);
        Assert.Equal(first, second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task Null_results_are_not_cached()
    {
        var (cache, _, _) = Create();
        var calls = 0;
        Task<Item?> Factory(CancellationToken _) { calls++; return Task.FromResult<Item?>(null); }

        await cache.GetOrCreateAsync("t:none", TimeSpan.FromMinutes(5), Factory);
        await cache.GetOrCreateAsync("t:none", TimeSpan.FromMinutes(5), Factory);

        Assert.Equal(2, calls);
        Assert.Empty(await cache.ListAsync());
    }

    [Fact]
    public async Task Keys_list_get_remove_prefix_and_clear()
    {
        var (cache, _, _) = Create();
        foreach (var key in new[] { "a:1", "a:2", "b:1" })
            await cache.GetOrCreateAsync(key, TimeSpan.FromMinutes(5), _ => Task.FromResult<Item?>(new Item(key, 1)));

        Assert.Equal(new[] { "a:1", "a:2", "b:1" }, (await cache.ListAsync()).Select(k => k.Key));
        Assert.Equal(new[] { "a:1", "a:2" }, (await cache.ListAsync("a:")).Select(k => k.Key));
        Assert.Contains("\"name\":\"b:1\"", (await cache.GetRawAsync("b:1"))!.Value.Json);

        Assert.True(await cache.RemoveAsync("b:1"));
        Assert.False(await cache.RemoveAsync("b:1"));
        Assert.Null(await cache.GetRawAsync("b:1"));
        Assert.Equal(2, await cache.RemoveByPrefixAsync("a:"));
        Assert.Empty(await cache.ListAsync());

        await cache.GetOrCreateAsync("c:1", TimeSpan.FromMinutes(5), _ => Task.FromResult<Item?>(new Item("c", 1)));
        Assert.Equal(1, await cache.ClearAsync());
        Assert.Empty(await cache.ListAsync());
    }

    [Fact]
    public async Task Expired_keys_are_not_listed()
    {
        var (_, adapter, time) = Create();
        await adapter.SetAsync("short", "1", TimeSpan.FromMinutes(1));
        await adapter.SetAsync("long", "2", TimeSpan.FromMinutes(10));

        time.Now = time.Now.AddMinutes(2);

        Assert.Equal(new[] { "long" }, (await adapter.ListAsync()).Select(k => k.Key));
        Assert.Null(await adapter.GetInfoAsync("short"));
    }

    [Fact]
    public async Task Setting_a_key_again_keeps_it_listed()
    {
        var (_, adapter, _) = Create();
        await adapter.SetAsync("k", "1", TimeSpan.FromMinutes(5));
        await adapter.SetAsync("k", "2", TimeSpan.FromMinutes(5));
        await Task.Delay(50); // eviction callbacks of the replaced entry run on the thread pool

        Assert.Equal("2", await adapter.GetAsync("k"));
        Assert.Single(await adapter.ListAsync());
    }
}
