using System.Diagnostics;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Ai;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Bookings;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Entities.Requests.Ai;
using Omni.App.Entities.Requests.Contacts;
using Omni.App.Entities.Requests.Knowledge;
using Omni.App.Entities.Responses.Ai;

namespace Omni.App.Business.Services.Ai;

/// <summary>
/// The AI bot's tools (see <see cref="IBotToolService"/>), moved out of AiController unchanged: same services, same answers.
///
/// WHICH TOOL FOR WHAT — rule of thumb: the Knowledge Base is for INFORMATION; the structured tools are for LIVE DATA and
/// ACTIONS. The Knowledge Base is never the source of truth for contact data, booking availability, bookings,
/// conversation state or handoff.
///
///   search_knowledge       → client-approved informational content (only the bot's sources): hours, address, policies,
///                            pricing, services, preparation, recovery, FAQs. (knowledge)
///   get_services           → the authoritative structured service records. (services)
///   get_contact_context    → what is currently known about THIS contact. (every bot)
///   update_contact         → the contact's language (every bot); update_contact_details → city, timeline, notes,
///                            follow-up, qualification (contact_update); update_stage → the pipeline stage (pipeline).
///   get_available_slots    → LIVE booking availability. (availability)
///   get_my_bookings, schedule_booking, cancel_booking → the contact's own bookings. (bookings)
///   reply, handoff_to_human → send the bot's reply / switch the conversation to the team. (every bot)
/// </summary>
public class BotToolService : IBotToolService
{
    private readonly IClientContext _clientContext;
    private readonly ICatalogService _services;
    private readonly IContactService _contacts;
    private readonly IBookingService _bookings;
    private readonly IAvailabilityService _availability;
    private readonly IKnowledgeSearchService _knowledgeSearch;
    private readonly IBotRuntimeService _runtime;
    private readonly IBotTestService _tests;
    private readonly IBotConversationService _botConversations;
    private readonly IEntitlementService _entitlements;
    private readonly IMessageTracer _tracer;

    public BotToolService(
        IClientContext clientContext, ICatalogService services, IContactService contacts,
        IBookingService bookings, IAvailabilityService availability, IKnowledgeSearchService knowledgeSearch,
        IBotRuntimeService runtime, IBotTestService tests, IBotConversationService botConversations,
        IEntitlementService entitlements, IMessageTracer tracer)
    {
        _clientContext = clientContext;
        _services = services;
        _contacts = contacts;
        _bookings = bookings;
        _availability = availability;
        _knowledgeSearch = knowledgeSearch;
        _runtime = runtime;
        _tests = tests;
        _botConversations = botConversations;
        _entitlements = entitlements;
        _tracer = tracer;
    }

    public async Task<BotToolResult> CallAsync(BotToolCall call, BotToolDefinition tool,
        Func<BotRunContext, CancellationToken, Task<BotToolResult>> body, CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        var status = StatusCodes.Status500InternalServerError;
        try
        {
            var result = await AuthorizeAndRunAsync(call, tool, body, ct);
            status = result.Status;
            return result;
        }
        finally
        {
            // Same step as TraceBotToolAttribute records for an /api/ai call.
            _tracer.Step(TraceSteps.BotToolCalled, status >= 400 ? TraceOutcomes.Failed : TraceOutcomes.Ok,
                new TraceIds(ClientId: call.ClientId == Guid.Empty ? null : call.ClientId, ConversationId: call.ConversationId),
                Stopwatch.GetElapsedTime(started).TotalMilliseconds, new { tool = tool.Route, status, contactId = call.ContactId });
        }
    }

    // BotToolAttribute, then RequireFeatureAttribute (AiTool = true), then the action.
    private async Task<BotToolResult> AuthorizeAndRunAsync(BotToolCall call, BotToolDefinition tool,
        Func<BotRunContext, CancellationToken, Task<BotToolResult>> body, CancellationToken ct)
    {
        var check = await _runtime.AuthorizeAsync(call.ClientId, call.BotId, call.TestSessionId, tool.Capability, ct);
        if (check.Run is null) return BotToolResult.Ok(BotToolOutcomes.NotAllowed(check.DeniedReason));

        if (tool.Feature is { } feature)
        {
            try
            {
                await _entitlements.EnsureFeatureAsync(call.ClientId, feature, ct);
            }
            catch (EntitlementDeniedException)
            {
                return BotToolResult.Ok(BotToolOutcomes.FeatureNotIncluded(feature));
            }
        }
        return await body(check.Run, ct);
    }

    public async Task<BotToolResult> SearchKnowledgeAsync(BotRunContext run, KnowledgeSearchRequest request, CancellationToken ct = default)
    {
        if (request.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(request.Query))
        {
            return BotToolResult.BadRequest(new { error = "clientId and query are required." }); // text-ok: read by the AI only
        }

        try
        {
            return BotToolResult.Ok(await _knowledgeSearch.SearchAsync(request.ClientId, request.Query, request.Limit, run.KnowledgeFilter, ct));
        }
        catch (InvalidOperationException ex)
        {
            // The embeddings provider failed: a 503 the AI can tell apart from "nothing found".
            return BotToolResult.Unavailable(new { error = ex.Message });
        }
    }

    // Partly overlaps the Knowledge Base (search_knowledge), which is now the preferred place for this kind of information;
    // kept working for now and may be retired later.
    public async Task<BotToolResult> GetClientInfoAsync(BotRunContext run, Guid clientId, CancellationToken ct = default)
    {
        var client = await _clientContext.GetByIdAsync(clientId, ct);
        if (client is null) return BotToolResult.NotFound();

        return BotToolResult.Ok(new ClientInfoResponse(
            client.Id, client.Name, client.Phone, client.Email, client.Website,
            client.Address, client.OperatingHours, client.BookingInfo, client.Timezone));
    }

    public async Task<BotToolResult> GetServicesAsync(BotRunContext run, Guid clientId, CancellationToken ct = default)
    {
        // Always ACTIVE services only — the AI must never see (and so never suggest or book) an inactive one. Historical use
        // of inactive services (existing contacts/bookings, campaign filtering) goes through the normal backend queries.
        return BotToolResult.Ok(await _services.ListAsync(clientId, activeOnly: true, ct));
    }

    public async Task<BotToolResult> GetContactContextAsync(BotRunContext run, Guid clientId, Guid contactId, CancellationToken ct = default)
    {
        if (run.IsTest) return BotToolResult.Ok(new { id = contactId, simulated = true, fullName = "Test contact" }); // text-ok: read by the AI only
        var contact = await _contacts.GetByIdAsync(clientId, contactId, ct);
        return contact is null ? BotToolResult.NotFound() : BotToolResult.Ok(contact);
    }

    public async Task<BotToolResult> UpdateContactAsync(BotRunContext run, Guid clientId, Guid contactId, UpdateContactContextRequest request, CancellationToken ct = default)
    {
        var (clean, missing) = _runtime.CheckContactUpdate(run, request);
        if (missing is not null) return BotToolResult.Ok(BotToolOutcomes.NotAllowed(BotToolCheck.NotAllowed));
        if (run.IsTest)
        {
            await _tests.NoteContactUpdateAsync(run, clean, ct);
            return BotToolResult.Ok(new { success = true, simulated = true });
        }
        var contact = await _contacts.UpdateContextAsync(clientId, contactId, clean, ct);
        return contact is null ? BotToolResult.NotFound() : BotToolResult.Ok(contact);
    }

    public async Task<BotToolResult> GetAvailableSlotsAsync(BotRunContext run, Guid clientId, Guid? contactId, Guid? serviceId, DateOnly? date, int? days, CancellationToken ct = default)
    {
        // With contactId (always passed by the workflow, never chosen by the AI) the answer also carries the contact's own
        // upcoming bookings and the backend's canCreateNewBooking decision — kept separate from the requested date and slots.
        // Read-only: this tool never books, reschedules or cancels. A test run's contact is the test session's.
        ContactBookingContext? context = null;
        if (run.IsTest)
        {
            context = await _tests.GetBookingContextAsync(run, ct);
        }
        else if (contactId is { } contact)
        {
            context = await _bookings.GetBookingContextAsync(clientId, contact, ct);
            if (context is null) return BotToolResult.NotFound(new { error = "Contact not found for this client." }); // text-ok: read by the AI only
        }

        var result = await _availability.GetSlotsAsync(clientId, serviceId, date, days ?? (date is null ? 7 : 1), ct);
        if (context is null) return BotToolResult.Ok(result);

        return BotToolResult.Ok(result with
        {
            RequestedDate = date?.ToString("yyyy-MM-dd") ?? result.Today,
            HasUpcomingBooking = context.HasUpcomingBooking,
            CanCreateNewBooking = context.CanCreateNewBooking,
            BookingBlockReason = context.BookingBlockReason,
            ExistingUpcomingBookings = context.ExistingUpcomingBookings
        });
    }

    public async Task<BotToolResult> ScheduleBookingAsync(BotRunContext run, Guid clientId, Guid contactId, ScheduleBookingRequest request, CancellationToken ct = default)
    {
        if (run.IsTest) return BotToolResult.Ok(await _tests.ScheduleAsync(run, request, ct));

        var o = await _bookings.ScheduleAsync(clientId, contactId, request, ct);
        var instruction = AiResultInstructions.ForSchedule(o.Code, o.Existing?.FirstOrDefault()?.Label, o.RequestedLabel);
        var changed = o.Code is "BOOKED" or "RESCHEDULED";

        return BotToolResult.Ok(new ScheduleBookingResult(
            changed, o.Operation, o.Code, o.Message, instruction,
            o.Booking, o.PreviousBooking,
            changed ? null : o.Existing,
            changed ? null : o.RequestedLabel));
    }

    public async Task<BotToolResult> GetMyBookingsAsync(BotRunContext run, Guid clientId, Guid contactId, CancellationToken ct = default)
    {
        if (run.IsTest) return BotToolResult.Ok(await _tests.GetUpcomingAsync(run, ct));
        return BotToolResult.Ok(await _bookings.GetUpcomingForContactAsync(clientId, contactId, ct));
    }

    public async Task<BotToolResult> CancelBookingAsync(BotRunContext run, Guid clientId, Guid contactId, Guid? bookingId, CancelBookingRequest request, CancellationToken ct = default)
    {
        if (run.IsTest) return BotToolResult.Ok(await _tests.CancelAsync(run, ct));

        var o = await _bookings.CancelBookingAsync(clientId, contactId, bookingId, request.Reason, ct);
        var instruction = AiResultInstructions.ForCancel(o.Code);

        return BotToolResult.Ok(new CancelBookingResult(
            o.Code == "CANCELED", o.Operation, o.Code, o.Message, instruction, o.Booking, o.Existing));
    }

    public async Task<BotToolResult> ReplyAsync(BotRunContext run, Guid conversationId, Guid clientId, AiReplyRequest request, CancellationToken ct = default)
    {
        // The workflow needs these two outcomes as 409s with a code (it stops without retrying), not as 422s.
        try
        {
            var outcome = await _botConversations.ReplyAsync(run, conversationId, request.Content, ct);
            return outcome is null ? BotToolResult.NotFound() : BotToolResult.Ok(outcome);
        }
        catch (ConversationNotInAiModeException ex)
        {
            return BotToolResult.Conflict(new { sent = false, code = "conversation_in_human_mode", conversationId, mode = ex.CurrentMode });
        }
        catch (AiReplyRefusedException ex)
        {
            return BotToolResult.Conflict(new { sent = false, code = "ai_reply_refused", reason = ex.Reason, conversationId, mode = ConversationMode.Human });
        }
    }

    public async Task<BotToolResult> HandoffAsync(BotRunContext run, Guid conversationId, Guid clientId, HandoffToHumanRequest request, CancellationToken ct = default)
    {
        var handedOff = await _botConversations.HandOffAsync(run, conversationId, request.Reason, ct);
        return handedOff is null ? BotToolResult.NotFound() : BotToolResult.Ok(new { handedOff });
    }
}
