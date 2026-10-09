using System.Text.Json;
using System.Text.Json.Serialization;
using PlasticSurgery.Common.Helpers;

namespace PlasticSurgery.Common.Converters;

public sealed class LenientNullableDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("A date/time must be a string.");
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
