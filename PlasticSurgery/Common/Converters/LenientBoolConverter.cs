using System.Text.Json;
using System.Text.Json.Serialization;
using PlasticSurgery.Common.Helpers;

namespace PlasticSurgery.Common.Converters;

/// <summary>A boolean the AI tool may send as true/false OR the strings "true"/"false" (empty/"null" = false).</summary>
public sealed class LenientBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True: return true;
            case JsonTokenType.False:
            case JsonTokenType.Null: return false;
            case JsonTokenType.String:
                var text = reader.GetString();
                if (LenientJson.IsEmpty(text)) return false;
                if (bool.TryParse(text, out var value)) return value;
                throw new JsonException($"'{text}' is not a valid true/false value.");
            default:
                throw new JsonException("Expected true or false.");
        }
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}
