using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Omni.App.Common.Helpers;

/// <summary>
/// JavaScript's JSON.stringify(value) for the AI prompts, so a value reads exactly as it did in the n8n workflow: compact,
/// keys in order, only <c>"</c>, <c>\</c> and control characters escaped (Arabic, emoji, &lt; and &amp; stay as they are,
/// unlike System.Text.Json's default escaping).
/// </summary>
public static class JsJson
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>A C# value as the n8n workflow received it (camelCase, the AI trigger's JSON), then JSON.stringify.</summary>
    public static string Stringify(object? value) =>
        Stringify(value is JsonNode node ? node : JsonSerializer.SerializeToNode(value, WebOptions));

    public static string Stringify(JsonNode? node)
    {
        var text = new StringBuilder();
        Write(text, node);
        return text.ToString();
    }

    private static void Write(StringBuilder text, JsonNode? node)
    {
        switch (node)
        {
            case null:
                text.Append("null");
                break;
            case JsonObject obj:
                text.Append('{');
                var first = true;
                foreach (var (key, value) in obj)
                {
                    if (!first) text.Append(',');
                    first = false;
                    WriteString(text, key);
                    text.Append(':');
                    Write(text, value);
                }
                text.Append('}');
                break;
            case JsonArray array:
                text.Append('[');
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0) text.Append(',');
                    Write(text, array[i]);
                }
                text.Append(']');
                break;
            default:
                var element = node.GetValue<JsonElement>();
                switch (element.ValueKind)
                {
                    case JsonValueKind.String:
                        WriteString(text, element.GetString()!);
                        break;
                    case JsonValueKind.Number:
                        text.Append(Number(element));
                        break;
                    default:
                        text.Append(element.GetRawText()); // true, false, null
                        break;
                }
                break;
        }
    }

    // JavaScript prints a number in its shortest round-trip form: 1, 1.5, 0.3 (never 1.0 or 3E-1 for ordinary values).
    private static string Number(JsonElement element)
    {
        if (element.TryGetInt64(out var whole)) return whole.ToString(CultureInfo.InvariantCulture);
        var value = element.GetDouble();
        return value == Math.Floor(value) && Math.Abs(value) < 1e21
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static void WriteString(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\b': text.Append("\\b"); break;
                case '\f': text.Append("\\f"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                case '\t': text.Append("\\t"); break;
                default:
                    if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else text.Append(c);
                    break;
            }
        }
        text.Append('"');
    }
}
