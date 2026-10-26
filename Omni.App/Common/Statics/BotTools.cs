using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Common.Statics;

/// <summary>
/// Every AI tool Omni serves (IBotToolService), with the /api/ai route template, the bot capability and the plan feature it
/// needs. AiController's [BotTool] and [RequireFeature] attributes say the same (BotToolServiceTests compares them).
/// </summary>
public static class BotTools
{
    public static readonly BotToolDefinition SearchKnowledge = new("api/ai/knowledge/search", BotCapability.Knowledge);
    public static readonly BotToolDefinition ClientInfo = new("api/ai/client-info", BotCapability.Knowledge);
    public static readonly BotToolDefinition Services = new("api/ai/services", BotCapability.Services);
    public static readonly BotToolDefinition ContactContext = new("api/ai/contacts/{contactId:guid}", null);
    /// <summary>update_contact / update_contact_details / update_stage: each field's capability is checked inside the tool.</summary>
    public static readonly BotToolDefinition UpdateContact = new("api/ai/contacts/{contactId:guid}", null);
    public static readonly BotToolDefinition AvailableSlots = new("api/ai/bookings/available", BotCapability.Availability, EntitlementKeys.Bookings);
    public static readonly BotToolDefinition ScheduleBooking = new("api/ai/bookings/schedule", BotCapability.Bookings, EntitlementKeys.Bookings);
    public static readonly BotToolDefinition MyBookings = new("api/ai/bookings/upcoming", BotCapability.Bookings, EntitlementKeys.Bookings);
    public static readonly BotToolDefinition CancelBooking = new("api/ai/bookings/cancel", BotCapability.Bookings, EntitlementKeys.Bookings);
    public static readonly BotToolDefinition Reply = new("api/ai/conversations/{id:guid}/reply", null);
    public static readonly BotToolDefinition Handoff = new("api/ai/conversations/{id:guid}/handoff", null);
}
