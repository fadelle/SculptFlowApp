using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Business.Contracts.Services.Configuration;
using PlasticSurgery.Common.Helpers;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Dtos.Configuration;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Configuration;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Configuration;

namespace PlasticSurgery.Business.Services.Configuration;

/// <summary>
/// Stored setting values (config.settings). Only settings declared in ConfigDefaults can be stored, and only values that
/// fit their type and range. After a change it refreshes IConfigManager, so the app uses the new value at once.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly ISettingRepository _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfigManager _config;

    public SettingsService(ISettingRepository settings, IUnitOfWork unitOfWork, IConfigManager config)
    {
        _settings = settings;
        _unitOfWork = unitOfWork;
        _config = config;
    }

    public async Task<IReadOnlyList<SettingResponse>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _settings.ListReadOnlyAsync(ct);
        return ConfigDefaults.All
            .OrderBy(d => d.Section).ThenBy(d => d.Key)
            .Select(d => ToResponse(d, rows.FirstOrDefault(r => Matches(r, d))))
            .ToList();
    }

    public async Task<SettingResponse> SetAsync(string section, string key, string? value, string? note, string actor, CancellationToken ct = default)
    {
        var definition = Declared(section, key);
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) throw new ArgumentException("Enter a value. To go back to the default, reset the setting instead.");
        if (value.Length > ConfigSettingLimits.MaxValueLength)
            throw new ArgumentException($"The value can be at most {ConfigSettingLimits.MaxValueLength} characters.");
        if (ConfigValues.Validate(definition, value) is { } problem) throw new ArgumentException(problem);
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note?.Length > ConfigSettingLimits.MaxDescriptionLength)
            throw new ArgumentException($"The note can be at most {ConfigSettingLimits.MaxDescriptionLength} characters.");

        var now = DateTimeOffset.UtcNow;
        var row = await _settings.GetForUpdateAsync(definition.Section, definition.Key, ct);
        if (row is null)
        {
            row = new ConfigSetting { Section = definition.Section, Key = definition.Key, CreatedAt = now };
            _settings.Add(row);
        }
        row.Value = value;
        row.Description = note;
        row.UpdatedBy = actor;
        row.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);
        await _config.RefreshAsync(ct);
        return ToResponse(definition, row);
    }

    public async Task<bool> ResetAsync(string section, string key, CancellationToken ct = default)
    {
        var definition = Declared(section, key);
        var row = await _settings.GetForUpdateAsync(definition.Section, definition.Key, ct);
        if (row is null) return false;
        _settings.Remove(row);
        await _unitOfWork.SaveChangesAsync(ct);
        await _config.RefreshAsync(ct);
        return true;
    }

    // ------------------------------------------------------------------------------------------------------------

    private static ConfigDefinition Declared(string? section, string? key) =>
        ConfigDefaults.Find((section ?? string.Empty).Trim(), (key ?? string.Empty).Trim())
        ?? throw new ArgumentException($"There is no setting {section}:{key}.");

    private static bool Matches(ConfigSetting row, ConfigDefinition d) =>
        string.Equals(row.Section, d.Section, StringComparison.OrdinalIgnoreCase) && string.Equals(row.Key, d.Key, StringComparison.OrdinalIgnoreCase);

    private SettingResponse ToResponse(ConfigDefinition d, ConfigSetting? row) =>
        new(d.Section, d.Key, d.Type.ToString(), d.Description, d.IsSecret ? "" : d.DefaultValue, Mask(d, row?.Value),
            Mask(d, _config.GetString(d)) ?? "", d.Min, d.Max, row?.Description, row?.UpdatedBy, row?.UpdatedAt, d.IsSecret);

    /// <summary>A secret is never sent back: only whether it has a value.</summary>
    private static string? Mask(ConfigDefinition d, string? value) =>
        !d.IsSecret || value is null ? value : value.Length == 0 ? "" : SecretMask;

    public const string SecretMask = "(set)";
}
