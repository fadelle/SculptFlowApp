using Omni.App.Common.Enums;

namespace Omni.App.Entities.Dtos.Configuration;

/// <summary>
/// One setting the app reads through IConfigManager: where it is stored (<see cref="Section"/>, <see cref="Key"/>), its
/// constant default, its type, an optional range for numbers and an optional regex <see cref="Pattern"/> a text value
/// must match. <see cref="IsSecret"/> settings (API keys) are stored like any other but never shown back: the settings API
/// returns only whether they are set, and their values never reach logs or the admin audit log. Declared in
/// Common/Statics/ConfigDefaults.
/// </summary>
public record ConfigDefinition(
    string Section,
    string Key,
    string DefaultValue,
    ConfigValueType Type,
    string Description,
    decimal? Min = null,
    decimal? Max = null,
    string? Pattern = null,
    bool IsSecret = false)
{
    /// <summary>The allowed values when <see cref="Pattern"/> is a plain list of choices (<c>^(n8n|code)$</c>), in order, so a
    /// form can offer them (the admin portal's Configuration page shows a dropdown); null otherwise.</summary>
    public IReadOnlyList<string>? Choices =>
        Pattern is { } pattern && System.Text.RegularExpressions.Regex.Match(pattern, @"^\^\(([\w.-]+(?:\|[\w.-]+)+)\)\$$") is { Success: true } m
            ? m.Groups[1].Value.Split('|')
            : null;
}
