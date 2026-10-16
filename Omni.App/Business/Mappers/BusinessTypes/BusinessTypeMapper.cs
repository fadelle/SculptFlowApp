using System.Text.Json;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.BusinessTypes;

namespace Omni.App.Business.Mappers.BusinessTypes;

/// <summary>
/// Reads and writes the per-language JSON columns of business types and stages, and picks a language with English as the
/// fallback. A column that isn't valid JSON (edited by hand) reads as empty rather than breaking every page.
/// </summary>
public static class BusinessTypeMapper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Dictionary<string, string> ParseTexts(string? json) =>
        TryParse<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();

    public static Dictionary<string, Dictionary<string, TermForms>> ParseTerminology(string? json) =>
        TryParse<Dictionary<string, Dictionary<string, TermForms>>>(json) ?? new Dictionary<string, Dictionary<string, TermForms>>();

    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, Json);

    /// <summary>The text in <paramref name="language"/>, else English, else <paramref name="fallback"/>.</summary>
    public static string Pick(IReadOnlyDictionary<string, string> texts, string language, string fallback) =>
        texts.TryGetValue(language, out var text) && !string.IsNullOrWhiteSpace(text) ? text
        : texts.TryGetValue(AppLanguage.English, out var english) && !string.IsNullOrWhiteSpace(english) ? english
        : fallback;

    public static string NameOf(BusinessType type, string language) => Pick(ParseTexts(type.NameJson), language, type.Code);

    public static string LabelOf(PipelineStage stage, string language) => Pick(ParseTexts(stage.LabelsJson), language, stage.Key);

    public static BusinessTypeOption ToOption(BusinessType type, string language) => new(type.Code, NameOf(type, language));

    public static PipelineStageOption ToOption(PipelineStage stage, string language) => new(stage.Key, stage.Category, LabelOf(stage, language));

    public static BusinessTypeResponse ToResponse(BusinessType type, int clientCount) => new(
        type.Code,
        ParseTexts(type.NameJson),
        ParseTerminology(type.TerminologyJson).ToDictionary(
            l => l.Key, l => (IReadOnlyDictionary<string, TermForms>)l.Value),
        type.AiProfile,
        type.IsActive,
        type.SortOrder,
        type.Stages.Select(s => new PipelineStageResponse(s.Key, s.Category, ParseTexts(s.LabelsJson), s.SortOrder)).ToList(),
        clientCount,
        type.CreatedAt,
        type.UpdatedAt);

    private static T? TryParse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); }
        catch (JsonException) { return null; }
    }
}
