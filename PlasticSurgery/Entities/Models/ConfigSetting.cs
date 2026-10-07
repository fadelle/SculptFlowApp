namespace PlasticSurgery.Entities.Models;

/// <summary>
/// One stored setting (config.settings), found by <see cref="Section"/> and <see cref="Key"/>. Its value replaces the
/// setting's constant default (Common/Statics/ConfigDefaults). Read through IConfigManager. Never holds secrets.
/// </summary>
public class ConfigSetting
{
    public string Section { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
