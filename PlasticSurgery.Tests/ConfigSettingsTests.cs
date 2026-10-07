using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Managers;
using PlasticSurgery.Business.Services.Configuration;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Helpers;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Dtos.Configuration;
using PlasticSurgery.Persistence.Contexts;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Configuration;
using PlasticSurgery.Persistence.Repositories;
using PlasticSurgery.Persistence.Repositories.Configuration;

namespace PlasticSurgery.Tests;

/// <summary>The configuration manager (config.settings by section + key, else the constant default) and the settings API rules.</summary>
[Collection("Postgres")]
public class ConfigSettingsTests
{
    private static readonly ConfigDefinition Duration = ConfigDefaults.AvailabilityDefaultDurationMinutes;

    private readonly PostgresFixture _db;

    public ConfigSettingsTests(PostgresFixture db) => _db = db;

    [Theory]
    [InlineData(ConfigValueType.Int, "45", true)]
    [InlineData(ConfigValueType.Int, "4.5", false)]
    [InlineData(ConfigValueType.Decimal, "0.5", true)]
    [InlineData(ConfigValueType.Bool, "true", true)]
    [InlineData(ConfigValueType.Bool, "yes", false)]
    public void Values_are_checked_against_the_type(ConfigValueType type, string value, bool valid) =>
        Assert.Equal(valid, ConfigValues.Validate(new ConfigDefinition("S", "K", "0", type, "test"), value) is null);

    [Fact]
    public void Numbers_are_checked_against_the_range()
    {
        Assert.NotNull(ConfigValues.Validate(Duration, "1"));   // below 5
        Assert.NotNull(ConfigValues.Validate(Duration, "999")); // above 480
        Assert.Null(ConfigValues.Validate(Duration, "45"));
    }

    [Fact]
    public void Every_declared_default_is_valid_and_unique()
    {
        foreach (var d in ConfigDefaults.All) Assert.Null(ConfigValues.Validate(d, d.DefaultValue));
        Assert.Equal(ConfigDefaults.All.Count, ConfigDefaults.All.Select(d => (d.Section.ToLowerInvariant(), d.Key.ToLowerInvariant())).Distinct().Count());
    }

    [Fact]
    public void Every_declared_setting_has_a_typed_property_and_every_property_a_declaration()
    {
        var properties = typeof(IConfigManager).GetProperties().ToDictionary(p => p.Name);
        foreach (var d in ConfigDefaults.All)
        {
            Assert.True(properties.TryGetValue(d.Section + d.Key, out var property), $"IConfigManager has no {d.Section}{d.Key} property.");
            var expected = d.Type switch
            {
                ConfigValueType.Int => typeof(int), ConfigValueType.Decimal => typeof(decimal), ConfigValueType.Bool => typeof(bool), _ => typeof(string)
            };
            Assert.Equal(expected, property!.PropertyType);
        }
        Assert.Equal(ConfigDefaults.All.Count, properties.Count);
    }

    [Fact]
    public void Typed_properties_return_the_defaults_without_a_database()
    {
        var config = new ConfigManager(null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigManager>.Instance);
        Assert.True(config.BillingEnabled);
        Assert.Equal(7, config.BillingGracePeriodDays);
        Assert.Equal(0.30m, config.KnowledgeMinScore);
        Assert.Equal(100, config.WebScrapingMaxPages);
        config.Apply([new Entities.Models.ConfigSetting { Section = "WebScraping", Key = "MaxPages", Value = "40" }]);
        Assert.Equal(40, config.WebScrapingMaxPages);
    }

    [PostgresFact]
    public async Task The_seed_script_stores_every_declared_setting_with_its_default_and_reruns_cleanly()
    {
        await ClearAsync();
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Database", "seed-config.sql"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var script = await File.ReadAllTextAsync(Path.Combine(dir!.FullName, "Database", "seed-config.sql"));
            await ExecAsync(script);
            await ExecAsync(script);

            await using var provider = Services();
            using var scope = provider.CreateScope();
            var rows = await scope.ServiceProvider.GetRequiredService<ISettingRepository>().ListReadOnlyAsync();
            Assert.Equal(ConfigDefaults.All.Count, rows.Count);
            foreach (var d in ConfigDefaults.All)
            {
                Assert.Equal(d.DefaultValue, rows.Single(r => r.Section == d.Section && r.Key == d.Key).Value);
            }
        }
        finally
        {
            await ClearAsync();
        }
    }

    [PostgresFact]
    public async Task Without_a_row_the_constant_default_is_used()
    {
        await using var provider = Services();
        await ClearAsync();
        var config = provider.GetRequiredService<IConfigManager>();
        await config.RefreshAsync();

        Assert.Equal(30, config.GetInt(Duration));
        Assert.Equal(30, config.GetInt("availability", "defaultdurationminutes"));
    }

    [PostgresFact]
    public async Task A_stored_value_wins_and_reset_restores_the_default()
    {
        await using var provider = Services();
        await ClearAsync();
        try
        {
            var config = provider.GetRequiredService<IConfigManager>();
            using (var scope = provider.CreateScope())
            {
                var response = await Service(scope).SetAsync("Availability", "DefaultDurationMinutes", "45", "longer consults", "tester");
                Assert.Equal("45", response.EffectiveValue);
                Assert.Equal("30", response.DefaultValue);
            }
            Assert.Equal(45, config.GetInt(Duration));

            using (var scope = provider.CreateScope())
            {
                Assert.True(await Service(scope).ResetAsync("Availability", "DefaultDurationMinutes"));
                Assert.False(await Service(scope).ResetAsync("Availability", "DefaultDurationMinutes"));
            }
            Assert.Equal(30, config.GetInt(Duration));
        }
        finally
        {
            await ClearAsync();
        }
    }

    [PostgresFact]
    public async Task A_secret_setting_is_used_but_never_returned()
    {
        await using var provider = Services();
        await ClearAsync();
        try
        {
            using var scope = provider.CreateScope();
            var response = await Service(scope).SetAsync("Embeddings", "ApiKey", "sk-test-not-real", null, "tester");
            Assert.True(response.IsSecret);
            Assert.Equal(SettingsService.SecretMask, response.StoredValue);
            Assert.Equal(SettingsService.SecretMask, response.EffectiveValue);
            Assert.DoesNotContain(await Service(scope).ListAsync(), r => r.StoredValue == "sk-test-not-real" || r.EffectiveValue == "sk-test-not-real");
            Assert.Equal("sk-test-not-real", provider.GetRequiredService<IConfigManager>().EmbeddingsApiKey);
        }
        finally
        {
            await ClearAsync();
        }
    }

    [PostgresFact]
    public async Task Undeclared_settings_and_invalid_values_are_refused()
    {
        await using var provider = Services();
        using var scope = provider.CreateScope();
        var service = Service(scope);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync("Nope", "Missing", "1", null, "tester"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync("Availability", "DefaultDurationMinutes", "abc", null, "tester"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetAsync("Availability", "DefaultDurationMinutes", "2", null, "tester"));
    }

    [PostgresFact]
    public async Task A_hand_written_invalid_row_falls_back_to_the_default()
    {
        await using var provider = Services();
        await ClearAsync();
        try
        {
            await ExecAsync("insert into config.settings (section, key, value) values ('Availability', 'DefaultDurationMinutes', 'lots')");
            var config = provider.GetRequiredService<IConfigManager>();
            await config.RefreshAsync();
            Assert.Equal(30, config.GetInt(Duration));
        }
        finally
        {
            await ClearAsync();
        }
    }

    [Fact]
    public async Task An_unreachable_database_leaves_the_defaults()
    {
        await using var provider = Services("Host=localhost;Port=1;Username=nobody;Timeout=1");
        var config = provider.GetRequiredService<IConfigManager>();
        await config.RefreshAsync();
        Assert.Equal(30, config.GetInt(Duration));
    }

    [Fact]
    public void An_undeclared_setting_is_a_programming_error()
    {
        using var provider = Services("Host=localhost;Port=1");
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IConfigManager>().GetInt("Nope", "Missing"));
    }

    // ------------------------------------------------------------------------------------------------------------

    private ServiceProvider Services(string? connectionString = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(connectionString ?? _db.ConnectionString));
        services.AddScoped<ISettingRepository, SettingRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IConfigManager, ConfigManager>();
        return services.BuildServiceProvider();
    }

    private static SettingsService Service(IServiceScope scope) =>
        new(scope.ServiceProvider.GetRequiredService<ISettingRepository>(), scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            scope.ServiceProvider.GetRequiredService<IConfigManager>());

    private Task ClearAsync() => ExecAsync("delete from config.settings");

    private async Task ExecAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
