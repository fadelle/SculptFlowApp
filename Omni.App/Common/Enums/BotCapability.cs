namespace Omni.App.Common.Enums;

/// <summary>crm.bots.capabilities: the Omni AI tools a bot may use. Checked by Omni on every tool call (BotToolAttribute),
/// not only written into the prompt. Handing off to the team, replying and saving the contact's language are always allowed.
/// </summary>
public static class BotCapability
{
    /// <summary>search_knowledge (and get_client_info): the Knowledge Base, limited to the bot's sources.</summary>
    public const string Knowledge = "knowledge";

    /// <summary>get_services: the client's active services.</summary>
    public const string Services = "services";

    /// <summary>get_available_slots: real open booking times.</summary>
    public const string Availability = "availability";

    /// <summary>get_my_bookings, schedule_booking, cancel_booking: the contact's own bookings.</summary>
    public const string Bookings = "bookings";

    /// <summary>update_contact_details: the contact's city, timeline, notes, follow-up and qualification.</summary>
    public const string ContactUpdate = "contact_update";

    /// <summary>update_stage: moves the contact to another pipeline stage.</summary>
    public const string Pipeline = "pipeline";

    /// <summary>In the order the bot editor lists them.</summary>
    public static readonly IReadOnlyList<string> All = [Knowledge, Services, Availability, Bookings, ContactUpdate, Pipeline];

    /// <summary>Capabilities that also need the plan's bookings feature.</summary>
    public static readonly IReadOnlySet<string> NeedBookings = new HashSet<string>(StringComparer.Ordinal) { Availability, Bookings };
}
