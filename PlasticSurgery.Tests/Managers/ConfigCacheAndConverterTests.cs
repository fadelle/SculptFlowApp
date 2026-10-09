using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PlasticSurgery.Business.Managers;
using PlasticSurgery.Business.Providers.Caching;
using PlasticSurgery.Common.Converters;

namespace PlasticSurgery.Tests.Managers;

public class ConfigManagerTests
{
    private readonly Mock<ISettingRepository> _repo = new();

    private ConfigManager Sut()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repo.Object);
        return new ConfigManager(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<ConfigManager>.Instance);
    }

    private static ConfigSetting Row(ConfigDefinition d, string value) => new() { Section = d.Section, Key = d.Key, Value = value };

    [Fact]
    public void Defaults_apply_until_a_row_is_stored()
    {
        var sut = Sut();
        Assert.Equal(int.Parse(ConfigDefaults.AvailabilityDefaultDurationMinutes.DefaultValue), sut.AvailabilityDefaultDurationMinutes);
        Assert.Equal(bool.Parse(ConfigDefaults.BillingEnabled.DefaultValue), sut.BillingEnabled);
        Assert.Equal(decimal.Parse(ConfigDefaults.KnowledgeMinScore.DefaultValue, System.Globalization.CultureInfo.InvariantCulture), sut.KnowledgeMinScore);
        Assert.Equal(ConfigDefaults.BillingCurrency.DefaultValue.Trim().ToUpperInvariant(), sut.BillingCurrency);
    }

    [Fact]
    public void Stored_values_override_case_insensitively()
    {
        var sut = Sut();
        sut.Apply([new ConfigSetting { Section = ConfigDefaults.AvailabilityDefaultDurationMinutes.Section.ToUpperInvariant(), Key = ConfigDefaults.AvailabilityDefaultDurationMinutes.Key.ToLowerInvariant(), Value = "45" }]);
        Assert.Equal(45, sut.AvailabilityDefaultDurationMinutes);
        Assert.Equal(45, sut.GetInt(ConfigDefaults.AvailabilityDefaultDurationMinutes.Section, ConfigDefaults.AvailabilityDefaultDurationMinutes.Key));
        sut.Apply([]);
        Assert.Equal(int.Parse(ConfigDefaults.AvailabilityDefaultDurationMinutes.DefaultValue), sut.AvailabilityDefaultDurationMinutes);
    }

    [Fact]
    public void An_invalid_stored_row_falls_back_to_the_default_instead_of_breaking_the_app()
    {
        var sut = Sut();
        sut.Apply([Row(ConfigDefaults.AvailabilityDefaultDurationMinutes, "not-a-number"), Row(ConfigDefaults.BillingEnabled, "maybe")]);
        Assert.Equal(int.Parse(ConfigDefaults.AvailabilityDefaultDurationMinutes.DefaultValue), sut.AvailabilityDefaultDurationMinutes);
        Assert.Equal(bool.Parse(ConfigDefaults.BillingEnabled.DefaultValue), sut.BillingEnabled);
    }

    [Fact]
    public void Typed_getters_by_name_and_unknown_settings()
    {
        var sut = Sut();
        sut.Apply([Row(ConfigDefaults.BillingEnabled, "true"), Row(ConfigDefaults.KnowledgeMinScore, "0.55"), Row(ConfigDefaults.BillingCurrency, "eur")]);
        Assert.True(sut.GetBool(ConfigDefaults.BillingEnabled.Section, ConfigDefaults.BillingEnabled.Key));
        Assert.Equal(0.55m, sut.GetDecimal(ConfigDefaults.KnowledgeMinScore.Section, ConfigDefaults.KnowledgeMinScore.Key));
        Assert.Equal("eur", sut.GetString(ConfigDefaults.BillingCurrency.Section, ConfigDefaults.BillingCurrency.Key));
        Assert.Equal("EUR", sut.BillingCurrency);
        Assert.Throws<InvalidOperationException>(() => sut.GetString("Nope", "Nothing"));
    }

    [Fact]
    public async Task Refresh_reads_the_repository_and_keeps_the_last_values_when_it_fails()
    {
        _repo.Setup(r => r.ListReadOnlyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ConfigSetting> { Row(ConfigDefaults.AvailabilityDefaultDurationMinutes, "50") });
        var sut = Sut();
        await sut.RefreshAsync();
        Assert.Equal(50, sut.AvailabilityDefaultDurationMinutes);

        _repo.Setup(r => r.ListReadOnlyAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
        await sut.RefreshAsync(); // swallowed, logged
        Assert.Equal(50, sut.AvailabilityDefaultDurationMinutes);

        _repo.Setup(r => r.ListReadOnlyAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.RefreshAsync());
    }

    [Fact]
    public void Every_declared_setting_has_a_working_typed_property_value()
    {
        var sut = Sut();
        var props = typeof(IConfigManager).GetProperties().Where(p => p.Name != "Item").ToList();
        foreach (var p in props) Assert.NotNull(p.GetValue(sut)); // each property parses its default
        Assert.True(props.Count >= ConfigDefaults.All.Count - 2);
    }

    [Fact]
    public void Rows_that_differ_only_by_case_do_not_break_the_snapshot()
    {
        var sut = Sut();
        var d = ConfigDefaults.AvailabilityDefaultDurationMinutes;
        sut.Apply([new ConfigSetting { Section = d.Section, Key = d.Key, Value = "40" }, new ConfigSetting { Section = d.Section.ToLowerInvariant(), Key = d.Key, Value = "41" }]);
    }
}

public class CacheManagerFailureTests
{
    private readonly Mock<ICacheAdapter> _adapter = new();

    private CacheManager Sut() => new(_adapter.Object, NullLogger<CacheManager>.Instance);

    private sealed record Item(string Name);

    [Fact]
    public async Task A_broken_cache_never_breaks_the_caller()
    {
        _adapter.Setup(a => a.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("redis down"));
        _adapter.Setup(a => a.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("redis down"));
        var value = await Sut().GetOrCreateAsync("k", TimeSpan.FromMinutes(1), _ => Task.FromResult<Item?>(new Item("fresh")));
        Assert.Equal("fresh", value!.Name);

        _adapter.Setup(a => a.RemoveAsync("k", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        _adapter.Setup(a => a.RemoveByPrefixAsync("p:", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
        Assert.False(await Sut().RemoveAsync("k"));
        Assert.Equal(0, await Sut().RemoveByPrefixAsync("p:"));
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed_and_raw_reads_need_both_info_and_value()
    {
        _adapter.Setup(a => a.GetAsync("k", It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => Sut().GetOrCreateAsync("k", TimeSpan.FromMinutes(1), _ => Task.FromResult<Item?>(new Item("x"))));
        _adapter.Setup(a => a.RemoveAsync("k", It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        await Assert.ThrowsAsync<OperationCanceledException>(() => Sut().RemoveAsync("k"));

        Assert.Null(await Sut().GetRawAsync("missing"));
        var info = new CacheEntryInfo("k2", DateTimeOffset.UtcNow.AddMinutes(1), 5);
        _adapter.Setup(a => a.GetInfoAsync("k2", It.IsAny<CancellationToken>())).ReturnsAsync(info);
        Assert.Null(await Sut().GetRawAsync("k2")); // info but value expired between calls
        _adapter.Setup(a => a.GetAsync("k2", It.IsAny<CancellationToken>())).ReturnsAsync("{\"a\":1}");
        var raw = await Sut().GetRawAsync("k2");
        Assert.Equal(("k2", "{\"a\":1}"), (raw!.Value.Info.Key, raw.Value.Json));
        await Sut().ListAsync("p");
        await Sut().ClearAsync();
        _adapter.Verify(a => a.ListAsync("p", It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class LenientConverterTests
{
    private sealed class Holder
    {
        [System.Text.Json.Serialization.JsonConverter(typeof(LenientBoolConverter))] public bool Flag { get; set; }
        [System.Text.Json.Serialization.JsonConverter(typeof(LenientNullableGuidConverter))] public Guid? Id { get; set; }
        [System.Text.Json.Serialization.JsonConverter(typeof(LenientNullableDateTimeOffsetConverter))] public DateTimeOffset? At { get; set; }
    }

    private static Holder Parse(string json) => JsonSerializer.Deserialize<Holder>(json)!;

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", false)]
    [InlineData("\"true\"", true)]
    [InlineData("\"FALSE\"", false)]
    [InlineData("\"\"", false)]
    [InlineData("\"null\"", false)]
    [InlineData("\"N/A\"", false)]
    public void Bool_accepts_the_ways_n8n_and_the_ai_send_booleans(string token, bool expected) =>
        Assert.Equal(expected, Parse("{\"Flag\":" + token + "}").Flag);

    [Theory]
    [InlineData("\"maybe\"")]
    [InlineData("5")]
    public void Bool_rejects_garbage(string token) =>
        Assert.Throws<JsonException>(() => Parse("{\"Flag\":" + token + "}"));

    [Fact]
    public void Guid_accepts_null_and_placeholder_strings_and_rejects_bad_ids()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, Parse("{\"Id\":\"" + id + "\"}").Id);
        foreach (var token in new[] { "null", "\"\"", "\"none\"", "\"undefined\"" }) Assert.Null(Parse("{\"Id\":" + token + "}").Id);
        Assert.Throws<JsonException>(() => Parse("{\"Id\":\"not-a-guid\"}"));
    }

    [Fact]
    public void Date_accepts_null_and_placeholders_and_rejects_bad_dates()
    {
        Assert.Equal(new DateTimeOffset(2026, 3, 5, 10, 0, 0, TimeSpan.Zero), Parse("{\"At\":\"2026-03-05T10:00:00Z\"}").At);
        foreach (var token in new[] { "null", "\"\"", "\"null\"" }) Assert.Null(Parse("{\"At\":" + token + "}").At);
        Assert.Throws<JsonException>(() => Parse("{\"At\":\"yesterday-ish\"}"));
    }

    [Fact]
    public void Writing_round_trips()
    {
        var id = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new Holder { Flag = true, Id = id, At = new DateTimeOffset(2026, 3, 5, 10, 0, 0, TimeSpan.Zero) });
        Assert.Contains("\"Flag\":true", json);
        Assert.Contains(id.ToString(), json);
        Assert.Contains("\"Id\":null", JsonSerializer.Serialize(new Holder()));
        Assert.Contains("\"At\":null", JsonSerializer.Serialize(new Holder()));
    }

    [Fact]
    public void A_number_where_an_id_is_expected_is_a_json_error_not_a_crash()
    {
        Assert.Throws<JsonException>(() => Parse("{\"Id\":5}"));
        Assert.Throws<JsonException>(() => Parse("{\"At\":5}"));
    }
}
