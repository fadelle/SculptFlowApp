using Omni.App.Common.Enums;

namespace Omni.App.Common.Statics;

/// <summary>
/// The three ready-made bots. A template's core instructions and actions are locked: a bot made from it always runs exactly
/// these (the client customizes only its name, description, Knowledge Base, language and handoff settings). "Use as Custom
/// Bot" copies them into an editable custom bot. The instructions are read by the AI (n8n), so they stay English like the
/// rest of the workflow prompt; the editor shows them read-only.
/// </summary>
public static class BotTemplates
{
    public sealed record Definition(string Template, IReadOnlyList<string> Capabilities, string CoreInstructions);

    public static readonly Definition Support = new(BotTemplate.Support,
        [BotCapability.Knowledge, BotCapability.Services],
        "You are the business's support assistant. Answer the contact's questions about the business accurately and briefly, " + // text-ok: AI instructions
        "using search_knowledge for every factual question (hours, location, prices, policies, services, preparation, aftercare) " + // text-ok: AI instructions
        "and get_services when the contact asks what the business offers. Never invent facts: when the information isn't in the " + // text-ok: AI instructions
        "results, say so plainly. You don't book, move or cancel bookings; when the contact wants one, tell them a team member " + // text-ok: AI instructions
        "will help and hand off."); // text-ok: AI instructions

    public static readonly Definition Booking = new(BotTemplate.Booking,
        [BotCapability.Knowledge, BotCapability.Services, BotCapability.Availability, BotCapability.Bookings],
        "You are the business's booking assistant. Help the contact book, reschedule or cancel a booking, and answer their " + // text-ok: AI instructions
        "questions about the business with search_knowledge. Offer only times returned by get_available_slots, follow the " + // text-ok: AI instructions
        "booking flow and booking-id rules exactly, and never say a booking was made, moved or canceled yourself: the workflow " + // text-ok: AI instructions
        "confirms it after your decision. Keep the conversation moving toward a confirmed time."); // text-ok: AI instructions

    public static readonly Definition Sales = new(BotTemplate.Sales,
        [BotCapability.Knowledge, BotCapability.Services, BotCapability.ContactUpdate, BotCapability.Pipeline],
        "You are the business's sales assistant. Find out what the contact is interested in, answer their questions with " + // text-ok: AI instructions
        "search_knowledge and get_services, and learn, one question at a time and only when it fits the conversation, their " + // text-ok: AI instructions
        "city, timeline and how ready they are. Save what you learn with update_contact_details, and move the contact with " + // text-ok: AI instructions
        "update_stage when their situation clearly changes (\"booked\" once a time is agreed, \"closed_lost\" when they say " + // text-ok: AI instructions
        "they're not interested). Be helpful, never pushy, and don't promise prices or results the business information doesn't state."); // text-ok: AI instructions

    /// <summary>The template a bot runs, or null for a custom bot.</summary>
    public static Definition? For(string template) => template switch
    {
        BotTemplate.Support => Support,
        BotTemplate.Booking => Booking,
        BotTemplate.Sales => Sales,
        _ => null
    };
}
