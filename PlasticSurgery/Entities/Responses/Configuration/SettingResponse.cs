namespace PlasticSurgery.Entities.Responses.Configuration;

/// <summary>
/// One declared setting. <see cref="DefaultValue"/> is the constant default, <see cref="StoredValue"/> the
/// config.settings row (null = none), <see cref="EffectiveValue"/> what the app uses now. <see cref="Type"/> is
/// String, Int, Decimal or Bool; <see cref="Min"/>/<see cref="Max"/> bound numbers. For an <see cref="IsSecret"/> setting
/// the values are never returned: StoredValue/EffectiveValue are "(set)" or null/empty.
/// </summary>
public record SettingResponse(
    string Section,
    string Key,
    string Type,
    string Description,
    string DefaultValue,
    string? StoredValue,
    string EffectiveValue,
    decimal? Min,
    decimal? Max,
    string? Note,
    string? UpdatedBy,
    DateTimeOffset? UpdatedAt,
    bool IsSecret);
