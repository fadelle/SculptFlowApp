using PlasticSurgery.Entities.Responses.Configuration;

namespace PlasticSurgery.Business.Contracts.Services.Configuration;

/// <summary>Stored setting values (config.settings) for the platform-admin API.</summary>
public interface ISettingsService
{
    /// <summary>Every setting declared in ConfigDefaults with its default, stored and effective value.</summary>
    Task<IReadOnlyList<SettingResponse>> ListAsync(CancellationToken ct = default);

    /// <summary>Stores a value for (section, key) and refreshes the configuration manager. Throws ArgumentException for an
    /// undeclared setting or a value that doesn't fit its type/range.</summary>
    Task<SettingResponse> SetAsync(string section, string key, string? value, string? note, string actor, CancellationToken ct = default);

    /// <summary>Removes the stored value, so the constant default applies again. False when there was none.</summary>
    Task<bool> ResetAsync(string section, string key, CancellationToken ct = default);
}
