using System.Text.Json;

namespace Omni.App.Entities.Dtos.Ai;

/// <summary>
/// The agent's final JSON (n8n "llm_result": every field read as text, like the Set node did). Null fields stay null; a
/// non-text value keeps its JSON text (true, 3).
/// </summary>
public sealed record BotDecision(
    string? Action,
    string? Message,
    string? ScheduledStart,
    string? ConfirmReplaceExisting,
    string? BookingId,
    string? ServiceId,
    string? Reason)
{
    public const string Reply = "reply";
    public const string Schedule = "schedule";
    public const string Cancel = "cancel";
    public const string Handoff = "handoff";

    /// <summary>JSON.parse(output): null when the output isn't one JSON object (n8n's llm_result failed then).</summary>
    public static BotDecision? TryParse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            return new BotDecision(Text(root, "action"), Text(root, "message"), Text(root, "scheduledStart"),
                Text(root, "confirmReplaceExisting"), Text(root, "bookingId"), Text(root, "serviceId"), Text(root, "reason"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        !root.TryGetProperty(name, out var value) ? null : value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            _ => value.GetRawText()
        };
}
