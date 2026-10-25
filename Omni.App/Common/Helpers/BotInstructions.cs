using System.Text;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;

namespace Omni.App.Common.Helpers;

/// <summary>
/// Writes the bot-specific part of the AI's system prompt (AiBotConfig.Instructions, sent with every AI trigger): the bot's
/// instructions, the actions it may use (Omni refuses any other tool call anyway), when it hands off and which language it
/// replies in. The n8n workflow's generic prompt (output contract, booking rules, style) wraps it. Read by the AI only, so
/// it stays English.
/// </summary>
public static class BotInstructions
{
    private static readonly IReadOnlyDictionary<string, string> Actions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [BotCapability.Knowledge] = "search_knowledge: answer questions from the business's information", // text-ok: AI prompt
        [BotCapability.Services] = "get_services: look up the business's services", // text-ok: AI prompt
        [BotCapability.Availability] = "get_available_slots: check real open booking times", // text-ok: AI prompt
        [BotCapability.Bookings] = "get_my_bookings and the schedule / cancel actions: book, move and cancel the contact's bookings", // text-ok: AI prompt
        [BotCapability.ContactUpdate] = "update_contact_details: save the contact's city, timeline, notes, follow-up date and qualification", // text-ok: AI prompt
        [BotCapability.Pipeline] = "update_stage: move the contact to another pipeline stage" // text-ok: AI prompt
    };

    public static string Compose(string name, string? description, string instructions, IReadOnlyList<string> capabilities,
        string handoffPolicy, string? language)
    {
        var text = new StringBuilder();
        text.Append("Your name: ").Append(name).Append('\n'); // text-ok: AI prompt
        if (!string.IsNullOrWhiteSpace(description)) text.Append("Your purpose: ").Append(description.Trim()).Append('\n'); // text-ok: AI prompt
        text.Append('\n').Append(instructions.Trim()).Append("\n\n");

        text.Append("ALLOWED ACTIONS (the system refuses every other tool and action):\n"); // text-ok: AI prompt
        foreach (var capability in capabilities)
        {
            if (Actions.TryGetValue(capability, out var line)) text.Append("- ").Append(line).Append('\n');
        }
        text.Append("- update_contact: save the language the contact writes in\n"); // text-ok: AI prompt
        text.Append("- reply and handoff are always allowed\n"); // text-ok: AI prompt
        if (!capabilities.Contains(BotCapability.Bookings) && !capabilities.Contains(BotCapability.Availability))
        {
            text.Append("You cannot check availability or book, move or cancel bookings: never return action=schedule or " + // text-ok: AI prompt
                "action=cancel; when the contact wants a booking, say a team member will help and follow the handoff policy.\n"); // text-ok: AI prompt
        }
        else if (!capabilities.Contains(BotCapability.Bookings))
        {
            text.Append("You may tell the contact which times are open, but you cannot book, move or cancel: never return " + // text-ok: AI prompt
                "action=schedule or action=cancel; when the contact picks a time, say a team member will confirm it and hand off.\n"); // text-ok: AI prompt
        }

        text.Append("\nHANDOFF POLICY:\n"); // text-ok: AI prompt
        text.Append(handoffPolicy == BotHandoffPolicy.OnRequest
            ? "Hand off (action=handoff) only when the contact asks for a person, a human or a team member; then always do. " + // text-ok: AI prompt
              "When you can't help with something, say so honestly and offer what you can, but don't hand off unless they ask.\n" // text-ok: AI prompt
            : "Hand off (action=handoff) when the contact asks for a person, a human or a team member (always), or when you " + // text-ok: AI prompt
              "can't confidently help: the answer isn't in the business's information, the request is outside your allowed " + // text-ok: AI prompt
              "actions, or the contact is upset or confused after your answer.\n"); // text-ok: AI prompt

        text.Append("\nREPLY LANGUAGE:\n"); // text-ok: AI prompt
        text.Append(language is not null && BotLanguages.EnglishNames.TryGetValue(language, out var fixedLanguage)
            ? $"Always reply in {fixedLanguage}, whatever language the contact writes in. This overrides the language rules below.\n" // text-ok: AI prompt
            : "Reply in the language and script of the contact's latest message (see the language rules below).\n"); // text-ok: AI prompt
        return text.ToString();
    }
}
