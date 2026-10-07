namespace PlasticSurgery.Common.Statics;

/// <summary>Limits and timing of the settings table (config.settings).</summary>
public static class ConfigSettingLimits
{
    /// <summary>How often the configuration manager re-reads config.settings (a change through the API reloads at once).</summary>
    public const int RefreshSeconds = 60;

    public const int MaxValueLength = 4000;
    public const int MaxDescriptionLength = 1000;
}
