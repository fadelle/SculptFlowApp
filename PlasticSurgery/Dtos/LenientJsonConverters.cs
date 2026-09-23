using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlasticSurgery.Dtos;

/// <summary>For optional values the AI's n8n tool fills in: LLMs send "" / "null" / "none" instead of omitting a field they have no
/// value for. Those mean "not provided" (null); a real value must still parse or the request is rejected as before.</summary>
internal static class LenientJson
{
    private static readonly HashSet<string> Empty = new(StringComparer.OrdinalIgnoreCase) { "", "null", "none", "n/a", "undefined" };

    public static bool IsEmpty(string? s) => s is null || Empty.Contains(s.Trim());
}

public sealed class LenientNullableGuidConverter : JsonConverter<Guid?>
{
    public override Guid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var text = reader.GetString();
        if (LenientJson.IsEmpty(text)) return null;
        return Guid.TryParse(text, out var id) ? id : throw new JsonException($"'{text}' is not a valid id.");
    }

    public override void Write(Utf8JsonWriter writer, Guid? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteStringValue(value.Value);
    }
}

public sealed class LenientNullableDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        var text = reader.GetString();
        if (LenientJson.IsEmpty(text)) return null;
        return DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var value) ? value : throw new JsonException($"'{text}' is not a valid date/time.");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteStringValue(value.Value);
    }
}
