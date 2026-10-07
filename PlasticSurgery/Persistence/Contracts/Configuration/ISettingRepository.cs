using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Configuration;

/// <summary>config.settings: stored setting values, one row per (section, key).</summary>
public interface ISettingRepository
{
    Task<IReadOnlyList<ConfigSetting>> ListReadOnlyAsync(CancellationToken ct = default);

    Task<ConfigSetting?> GetForUpdateAsync(string section, string key, CancellationToken ct = default);

    void Add(ConfigSetting setting);

    void Remove(ConfigSetting setting);
}
