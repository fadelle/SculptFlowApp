using System.Globalization;
using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Common.Helpers;

/// <summary>
/// The values of the AI prompt placeholders (AiPromptTemplate), worked out from the AI trigger exactly as the n8n
/// expressions did (tools/extract-n8n-prompts.ps1 lists which expression became which placeholder):
/// <list type="bullet">
/// <item>a plain field (clientName, aiProfile, bot.instructions, language, conversationId, messageText): its text, empty
/// when missing;</item>
/// <item><c>contactLanguage || 'not known yet'</c>, <c>timezone || 'UTC'</c>: the fallback when missing or empty;</item>
/// <item><c>JSON.stringify(terms)</c>, <c>JSON.stringify($json)</c>: JsJson;</item>
/// <item><c>$now.setZone(timezone || 'UTC').toFormat('cccc, yyyy-MM-dd HH:mm')</c>: the business-local time, English
/// weekday ("Wednesday, 2026-07-15 12:05"); a timezone .NET doesn't know falls back to UTC.</item>
/// </list>
/// </summary>
public static class AiPromptValues
{
    public const string NoContactLanguage = "not known yet";
    public const string DefaultTimezone = "UTC";

    /// <summary>The agent's placeholders (and the formatter's system prompt's, a subset) for one contact message.</summary>
    public static IReadOnlyDictionary<string, string> ForMessage(AiTriggerPayload trigger, DateTimeOffset now)
    {
        var timezone = Or(trigger.Timezone, DefaultTimezone);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["clientName"] = trigger.ClientName ?? "",
            ["botInstructions"] = trigger.Bot?.Instructions ?? "",
            ["aiProfile"] = trigger.AiProfile ?? "",
            ["termsJson"] = JsJson.Stringify(trigger.Terms),
            ["language"] = trigger.Language ?? "",
            ["contactLanguage"] = Or(trigger.ContactLanguage, NoContactLanguage),
            ["timezone"] = timezone,
            ["now"] = LocalNow(now, timezone),
            ["conversationId"] = trigger.ConversationId.ToString(),
            ["messageText"] = trigger.MessageText ?? ""
        };
    }

    /// <summary>The formatter's placeholders: the message's, plus the verified backend result as the formatter reads it.</summary>
    public static IReadOnlyDictionary<string, string> ForFormatter(AiTriggerPayload trigger, DateTimeOffset now, object? result) =>
        new Dictionary<string, string>(ForMessage(trigger, now), StringComparer.Ordinal) { ["resultJson"] = JsJson.Stringify(result) };

    /// <summary>"cccc, yyyy-MM-dd HH:mm" in the business's timezone.</summary>
    public static string LocalNow(DateTimeOffset now, string timezone)
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out var found) ? found : TimeZoneInfo.Utc;
        return TimeZoneInfo.ConvertTime(now, zone).ToString("dddd, yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    // JavaScript's `value || fallback` for a text: null and "" are falsy.
    private static string Or(string? value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;
}
