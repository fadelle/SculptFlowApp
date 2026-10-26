using Omni.App.Controllers.Filters;
using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Tests.Support;

/// <summary>
/// Every schedule_booking / cancel_booking outcome as the AI tool answers it, taken over from AiTestController (the n8n
/// test endpoints): same shapes, same AiResultInstructions wording, the TOOL_NOT_ALLOWED refusal and, beyond it, the
/// FEATURE_NOT_INCLUDED answer of a plan without bookings.
/// </summary>
internal static class CannedToolResults
{
    private static readonly Guid FakeServiceId = Guid.Parse("00000000-0000-0000-0000-0000000000bb");

    public static readonly string[] ScheduleCodes =
        ["BOOKED", "RESCHEDULED", "CONFIRMATION_REQUIRED", "MULTIPLE_UPCOMING_BOOKINGS", "SLOT_UNAVAILABLE", "CONTACT_NOT_FOUND", "INVALID_REQUEST",
         BotToolOutcomes.NotAllowedCode, BotToolOutcomes.FeatureNotIncludedCode];

    public static readonly string[] CancelCodes =
        ["CANCELED", "NO_UPCOMING_BOOKING", "MULTIPLE_UPCOMING_BOOKINGS", "INVALID_REQUEST", BotToolOutcomes.NotAllowedCode, BotToolOutcomes.FeatureNotIncludedCode];

    public static object Schedule(string code)
    {
        if (code == BotToolOutcomes.NotAllowedCode) return BotToolAttribute.NotAllowed(BotToolCheck.NotAllowed);
        if (code == BotToolOutcomes.FeatureNotIncludedCode) return BotToolOutcomes.FeatureNotIncluded(EntitlementKeys.Bookings);
        var target = Existing("Monday, Sep 28 at 3:00 PM", "2026-09-28", "15:00");
        const string requestedLabel = "Tuesday, Sep 29 at 4:00 PM";
        return code switch
        {
            "BOOKED" => new ScheduleBookingResult(true, "created", "BOOKED", "Booking made.", Booking: Existing(requestedLabel, "2026-09-29", "16:00")),
            "RESCHEDULED" => new ScheduleBookingResult(true, "rescheduled", "RESCHEDULED", "Booking rescheduled.",
                Booking: Existing(requestedLabel, "2026-09-29", "16:00"), PreviousBooking: target),
            "CONFIRMATION_REQUIRED" => Fail("CONFIRMATION_REQUIRED", "Contact already has an upcoming booking; nothing was changed.", [target], requestedLabel),
            "MULTIPLE_UPCOMING_BOOKINGS" => Fail("MULTIPLE_UPCOMING_BOOKINGS", "Contact has more than one upcoming booking; it is unclear which to move.",
                [target, Existing("Friday, Oct 2 at 10:00 AM", "2026-10-02", "10:00")], requestedLabel),
            "SLOT_UNAVAILABLE" => Fail("SLOT_UNAVAILABLE", "That time is no longer available - it overlaps another booking.", null, requestedLabel),
            "CONTACT_NOT_FOUND" => Fail("CONTACT_NOT_FOUND", "Contact not found for this client.", null, null),
            "INVALID_REQUEST" => Fail("INVALID_REQUEST", "That booking is not one of this contact's current upcoming bookings.", [target], requestedLabel),
            _ => throw new ArgumentException(code)
        };

        static ScheduleBookingResult Fail(string c, string message, UpcomingBookingResponse[]? existing, string? label) =>
            new(false, "none", c, message, AiResultInstructions.ForSchedule(c, existing?.FirstOrDefault()?.Label, label), null, null, existing, label);
    }

    public static object Cancel(string code)
    {
        if (code == BotToolOutcomes.NotAllowedCode) return BotToolAttribute.NotAllowed(BotToolCheck.NotAllowed);
        if (code == BotToolOutcomes.FeatureNotIncludedCode) return BotToolOutcomes.FeatureNotIncluded(EntitlementKeys.Bookings);
        var target = Existing("Monday, Sep 28 at 11:00 AM", "2026-09-28", "11:00");
        return code switch
        {
            "CANCELED" => new CancelBookingResult(true, "canceled", "CANCELED", "Booking canceled.", Booking: target with { Status = "canceled" }),
            "NO_UPCOMING_BOOKING" => Fail("NO_UPCOMING_BOOKING", "This contact has no upcoming booking to cancel (it may already be canceled).", null),
            "MULTIPLE_UPCOMING_BOOKINGS" => Fail("MULTIPLE_UPCOMING_BOOKINGS", "Contact has more than one upcoming booking; it is unclear which to cancel.",
                [target, Existing("Friday, Oct 2 at 10:00 AM", "2026-10-02", "10:00")]),
            "INVALID_REQUEST" => Fail("INVALID_REQUEST", "That booking is not one of this contact's current upcoming bookings.", [target]),
            _ => throw new ArgumentException(code)
        };

        static CancelBookingResult Fail(string c, string message, UpcomingBookingResponse[]? existing) =>
            new(false, "none", c, message, AiResultInstructions.ForCancel(c), null, existing);
    }

    private static UpcomingBookingResponse Existing(string label, string date, string time) => new(
        Guid.Parse("00000000-0000-0000-0000-00000000b00c"), "booked", "standard", FakeServiceId, "Consult",
        DateTimeOffset.Parse($"{date}T{time}:00+03:00"), DateTimeOffset.Parse($"{date}T{time}:00+03:00").AddMinutes(30), date, time, label);
}
