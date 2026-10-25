using System.Text.Json;
using Omni.App.Business.Contracts.Engines.Billing;
using Omni.App.Business.Contracts.HttpClients.N8n;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Ai;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Bookings;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Dtos.Bookings;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Ai;
using Omni.App.Entities.Requests.Contacts;
using Omni.App.Entities.Responses.Bookings;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Ai;
using Omni.App.Persistence.Contracts.Clients;

namespace Omni.App.Business.Services.Ai;

public class BotTestService : IBotTestService
{
    /// <summary>AiTriggerPayload.Channel of a test run (it has no real channel).</summary>
    public const string TestChannel = "test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IBotRepository _bots;
    private readonly IBotTestRepository _tests;
    private readonly IBotRuntimeService _runtime;
    private readonly IEntitlementService _entitlements;
    private readonly IAiTriggerNotifier _trigger;
    private readonly IMessageBillingService _billing;
    private readonly IAvailabilityService _availability;
    private readonly IClientRepository _clients;
    private readonly IConfigManager _config;
    private readonly IUnitOfWork _unitOfWork;

    public BotTestService(IBotRepository bots, IBotTestRepository tests, IBotRuntimeService runtime, IEntitlementService entitlements,
        IAiTriggerNotifier trigger, IMessageBillingService billing, IAvailabilityService availability, IClientRepository clients,
        IConfigManager config, IUnitOfWork unitOfWork)
    {
        _bots = bots;
        _tests = tests;
        _runtime = runtime;
        _entitlements = entitlements;
        _trigger = trigger;
        _billing = billing;
        _availability = availability;
        _clients = clients;
        _config = config;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> SendAsync(Guid clientId, Guid botId, Guid? sessionId, string? text, CancellationToken ct = default)
    {
        var message = string.IsNullOrWhiteSpace(text) ? throw new ArgumentException("bots.test.errors.textRequired") : text.Trim();
        var max = _config.BotsTestMessageMaxLength;
        if (message.Length > max) throw new ArgumentException("bots.test.errors.tooLong").WithArgs(max);

        var bot = (await _bots.ListForClientReadOnlyAsync(clientId, ct)).FirstOrDefault(b => b.Id == botId)
            ?? throw new KeyNotFoundException("bots.errors.notFound");
        await _entitlements.EnsureFeatureAsync(clientId, EntitlementKeys.AiAssistant, ct);
        if (BotRules.NotReadyKey(bot) is { } notReady) throw new InvalidOperationException(notReady);

        var now = DateTimeOffset.UtcNow;
        BotTestSession session;
        if (sessionId is { } id)
        {
            session = await _tests.GetSessionAsync(clientId, id, ct) is { } found && found.BotId == botId
                ? found
                : throw new KeyNotFoundException("bots.test.errors.sessionNotFound");
            if (session.HandedOff) throw new InvalidOperationException("bots.test.errors.handedOff");
        }
        else
        {
            await _tests.DeleteSessionsOlderThanAsync(now.AddHours(-_config.BotsTestSessionHours), ct);
            session = new BotTestSession { Id = Guid.NewGuid(), ClientId = clientId, BotId = botId, CreatedAt = now };
            _tests.AddSession(session);
        }
        session.UpdatedAt = now;
        var line = Add(session, BotTestRole.Contact, message, now);
        await _unitOfWork.SaveChangesAsync(ct);

        var config = await _runtime.ConfigAsync(clientId, botId, ct) ?? throw new KeyNotFoundException("bots.errors.notFound");
        var accepted = await _trigger.NotifyTestAsync(new AiTriggerPayload(clientId, session.Id, ContactId: session.Id, MessageId: line.Id,
            Channel: TestChannel, MessageType: "text", MessageText: message, Bot: config, TestSessionId: session.Id), ct);
        if (!accepted)
        {
            AddNote(session, BotTestNote.Of(BotTestNote.NotReachableKey), DateTimeOffset.UtcNow);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        return session.Id;
    }

    public async Task<(BotTestSession Session, IReadOnlyList<BotTestMessage> Lines)?> GetAsync(Guid clientId, Guid botId, Guid sessionId,
        CancellationToken ct = default)
    {
        var session = await _tests.GetSessionAsync(clientId, sessionId, ct);
        if (session is null || session.BotId != botId) return null;
        return (session, await _tests.ListMessagesAsync(session.Id, ct));
    }

    public async Task ResetAsync(Guid clientId, Guid sessionId, CancellationToken ct = default) =>
        await _tests.DeleteSessionAsync(clientId, sessionId, ct);

    public async Task<bool> ReplyAsync(BotRunContext run, string? content, CancellationToken ct = default)
    {
        var text = string.IsNullOrWhiteSpace(content) ? throw new ArgumentException("inbox.errors.contentRequired") : content.Trim();
        var session = await SessionAsync(run, ct);

        // Charged like a live AI reply (ai_reply: included credit, then wallet); there is no channel message to pay for.
        var messageId = Guid.NewGuid();
        MessageBillingHold? hold;
        try
        {
            hold = await _billing.ReserveAiReplyAsync(run.ClientId, null, messageId, ct);
        }
        catch (BillingDeniedException)
        {
            AddNote(session, BotTestNote.Of(BotTestNote.ReplyRefusedKey), DateTimeOffset.UtcNow);
            await _unitOfWork.SaveChangesAsync(ct);
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        _tests.AddMessage(new BotTestMessage { Id = messageId, SessionId = session.Id, ClientId = run.ClientId, Role = BotTestRole.Bot, Content = text, CreatedAt = now });
        session.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);
        await _billing.SentAsync(hold, ct);
        return true;
    }

    public async Task<bool> HandoffAsync(BotRunContext run, string? reason, CancellationToken ct = default)
    {
        // Row lock: a workflow retry (or two handoff calls) for the same test can't both add the handoff message.
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var session = await _tests.GetSessionForUpdateAsync(run.ClientId, run.TestSessionId!.Value, ct)
            ?? throw new KeyNotFoundException("bots.test.errors.sessionNotFound");
        if (session.HandedOff)
        {
            await transaction.CommitAsync(ct);
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        session.HandedOff = true;
        session.UpdatedAt = now;
        AddNote(session, string.IsNullOrWhiteSpace(reason)
            ? BotTestNote.Of(BotTestNote.HandedOffNoReasonKey)
            : BotTestNote.Of(BotTestNote.HandedOffKey, reason.Trim()), now);
        if (!string.IsNullOrWhiteSpace(run.HandoffMessage)) Add(session, BotTestRole.Automatic, run.HandoffMessage, now.AddMilliseconds(1));
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<ScheduleBookingResult> ScheduleAsync(BotRunContext run, ScheduleBookingRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = await SessionAsync(run, ct);
        var tz = await TimeZoneAsync(run.ClientId, ct);
        var requested = TimeZoneInfo.ConvertTime(request.ScheduledStart, tz);
        var requestedLabel = BookingLabels.For(requested);
        var existing = Existing(session, tz);

        if (existing is not null && !request.ConfirmReplaceExisting)
        {
            const string code = "CONFIRMATION_REQUIRED";
            return new ScheduleBookingResult(false, "none", code, "Contact already has an upcoming booking; nothing was changed.", // text-ok: read by the AI
                AiResultInstructions.ForSchedule(code, existing.Label, requestedLabel), ExistingUpcomingBookings: [existing], RequestedLabel: requestedLabel);
        }

        // The real open slots decide, exactly like the live tool; nothing is written to the bookings.
        var slots = await _availability.GetSlotsAsync(run.ClientId, request.ServiceId, DateOnly.FromDateTime(requested.DateTime), 1, ct);
        var slot = slots.Slots.FirstOrDefault(s => s.Start.UtcDateTime == request.ScheduledStart.UtcDateTime);
        if (!slots.Configured || slot is null)
        {
            const string code = "SLOT_UNAVAILABLE";
            return new ScheduleBookingResult(false, "none", code, "That time is not available.", // text-ok: read by the AI
                AiResultInstructions.ForSchedule(code, existing?.Label, requestedLabel), ExistingUpcomingBookings: existing is null ? null : [existing],
                RequestedLabel: requestedLabel);
        }

        var now = DateTimeOffset.UtcNow;
        session.SimulatedBookingStart = slot.Start;
        session.SimulatedBookingEnd = slot.End;
        session.SimulatedServiceId = request.ServiceId;
        session.UpdatedAt = now;
        var booking = Existing(session, tz)!;
        AddNote(session, BotTestNote.Of(existing is null ? BotTestNote.BookedKey : BotTestNote.RescheduledKey, booking.Label), now);
        await _unitOfWork.SaveChangesAsync(ct);

        return existing is null
            ? new ScheduleBookingResult(true, "created", "BOOKED", "Booking made.", Booking: booking) // text-ok: read by the AI
            : new ScheduleBookingResult(true, "rescheduled", "RESCHEDULED", "Booking rescheduled.", Booking: booking, PreviousBooking: existing); // text-ok: read by the AI
    }

    public async Task<CancelBookingResult> CancelAsync(BotRunContext run, CancellationToken ct = default)
    {
        var session = await SessionAsync(run, ct);
        var existing = Existing(session, await TimeZoneAsync(run.ClientId, ct));
        if (existing is null)
        {
            const string code = "NO_UPCOMING_BOOKING";
            return new CancelBookingResult(false, "none", code, "This contact has no upcoming booking to cancel (it may already be canceled).", // text-ok: read by the AI
                AiResultInstructions.ForCancel(code));
        }

        var now = DateTimeOffset.UtcNow;
        session.SimulatedBookingStart = null;
        session.SimulatedBookingEnd = null;
        session.SimulatedServiceId = null;
        session.UpdatedAt = now;
        AddNote(session, BotTestNote.Of(BotTestNote.CanceledKey, existing.Label), now);
        await _unitOfWork.SaveChangesAsync(ct);
        return new CancelBookingResult(true, "canceled", "CANCELED", "Booking canceled.", Booking: existing with { Status = "canceled" }); // text-ok: read by the AI
    }

    public async Task<UpcomingBookingsResponse> GetUpcomingAsync(BotRunContext run, CancellationToken ct = default)
    {
        var session = await SessionAsync(run, ct);
        var tz = await TimeZoneAsync(run.ClientId, ct);
        var existing = Existing(session, tz);
        return new UpcomingBookingsResponse(tz.Id, existing is null ? [] : [existing]);
    }

    public async Task<ContactBookingContext> GetBookingContextAsync(BotRunContext run, CancellationToken ct = default)
    {
        var upcoming = (await GetUpcomingAsync(run, ct)).Bookings;
        var blocked = upcoming.Count > 0;
        return new ContactBookingContext(blocked, !blocked, blocked ? "existing_upcoming_booking" : null, upcoming);
    }

    public async Task NoteContactUpdateAsync(BotRunContext run, UpdateContactContextRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var details = request.ServiceId is not null || request.City is not null || request.DesiredTimeline is not null || request.Notes is not null
            || request.NextFollowupAt is not null || request.Qualification is not null;
        if (!details && request.Stage is null) return; // the contact's language: housekeeping, not worth a note

        var session = await SessionAsync(run, ct);
        var now = DateTimeOffset.UtcNow;
        if (details) AddNote(session, BotTestNote.Of(BotTestNote.ContactUpdatedKey), now);
        if (request.Stage is not null) AddNote(session, BotTestNote.Of(BotTestNote.StageMovedKey, request.Stage), now.AddMilliseconds(1));
        session.UpdatedAt = now;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<BotTestSession> SessionAsync(BotRunContext run, CancellationToken ct) =>
        await _tests.GetSessionAsync(run.ClientId, run.TestSessionId ?? Guid.Empty, ct)
        ?? throw new KeyNotFoundException("bots.test.errors.sessionNotFound");

    private async Task<TimeZoneInfo> TimeZoneAsync(Guid clientId, CancellationToken ct) =>
        ViewerTimeZone.Find((await _clients.GetReadOnlyAsync(clientId, ct))?.Timezone) ?? TimeZoneInfo.Utc;

    /// <summary>The test contact's simulated booking as the booking tools show one (still upcoming), else null.</summary>
    private static UpcomingBookingResponse? Existing(BotTestSession session, TimeZoneInfo tz)
    {
        if (session.SimulatedBookingStart is not { } start || start <= DateTimeOffset.UtcNow) return null;
        var local = TimeZoneInfo.ConvertTime(start, tz);
        DateTimeOffset? end = session.SimulatedBookingEnd is { } e ? TimeZoneInfo.ConvertTime(e, tz) : null;
        return new UpcomingBookingResponse(session.Id, "booked", "standard", session.SimulatedServiceId, null, local, end,
            local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            local.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), BookingLabels.For(local));
    }

    private BotTestMessage Add(BotTestSession session, string role, string content, DateTimeOffset at)
    {
        var line = new BotTestMessage { Id = Guid.NewGuid(), SessionId = session.Id, ClientId = session.ClientId, Role = role, Content = content, CreatedAt = at };
        _tests.AddMessage(line);
        return line;
    }

    private void AddNote(BotTestSession session, BotTestNote note, DateTimeOffset at) =>
        Add(session, BotTestRole.Note, JsonSerializer.Serialize(note, Json), at);
}
