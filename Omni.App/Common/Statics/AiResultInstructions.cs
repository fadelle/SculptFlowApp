namespace Omni.App.Common.Statics;

/// <summary>The exact instruction text schedule_booking/cancel_booking attach to a failing result — the single place
/// this wording lives, shared by the real AI endpoints (AiController) and the test-only AiTestController (see its class doc) so
/// a tool-description test against the fake endpoints exercises the same words the AI would really see.</summary>
public static class AiResultInstructions
{
    private const string NothingChanged = "Nothing was booked or changed. Do NOT tell the contact a booking was booked, confirmed or moved. ";
    private const string NothingCanceled = "Nothing was canceled. Do NOT tell the contact a booking was canceled. ";

    public static string? ForSchedule(string code, string? existingLabel, string? requestedLabel) => code switch
    {
        "BOOKED" or "RESCHEDULED" => null,
        "CONFIRMATION_REQUIRED" => NothingChanged +
            $"The contact already has a booking on {existingLabel}. Tell them so, and ask whether they want to MOVE it to {requestedLabel}. " +
            "Only if they clearly agree, call schedule_booking again with the same scheduledStart and confirmReplaceExisting=true. " +
            "If they don't want to move it, do not call it again.",
        "MULTIPLE_UPCOMING_BOOKINGS" => NothingChanged +
            "The contact has several upcoming bookings (see existingUpcomingBookings). Ask which one to move, then call schedule_booking again " +
            "with confirmReplaceExisting=true and that booking's id as bookingId.",
        "SLOT_UNAVAILABLE" => NothingChanged +
            "Apologise briefly, call get_available_slots again, and offer the contact other times. Do not retry the same time.",
        _ => NothingChanged + "Tell the contact you couldn't complete this and that a team member will help shortly."
    };

    public static string? ForCancel(string code) => code switch
    {
        "CANCELED" => null,
        "NO_UPCOMING_BOOKING" =>
            NothingCanceled + "Tell the contact you couldn't find an upcoming booking to cancel (it may already be canceled). You may call get_my_bookings to check.",
        "MULTIPLE_UPCOMING_BOOKINGS" =>
            NothingCanceled + "The contact has several upcoming bookings (see existingUpcomingBookings). Ask which one to cancel, confirm it, then call cancel_booking again with that booking's id as bookingId.",
        _ => NothingCanceled + "Tell the contact you couldn't cancel it and that a team member will help shortly."
    };
}
