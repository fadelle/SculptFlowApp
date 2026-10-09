using System.Text.Json;
using System.Text.Json.Serialization;
using PlasticSurgery.Common.Helpers;

namespace PlasticSurgery.Common.Converters;

public sealed class LenientNullableGuidConverter : JsonConverter<Guid?>
{
    public override Guid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("An id must be a string.");
        var text = reader.GetString();
        if (LenientJson.IsEmpty(text)) return null;
        return Guid.TryParse(text, out var id) ? id : throw new JsonException($"'{text}' is not a valid id.");
    }

    public override void Write(Utf8JsonWriter writer, Guid? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteStringValue(value.Value);
    }
}
