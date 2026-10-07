namespace PlasticSurgery.Common.Statics;

/// <summary>The exact instruction text schedule_consultation/cancel_consultation attach to a failing result — the single place
/// this wording lives, shared by the real AI endpoints (AiController) and the test-only AiTestController (see its class doc) so
/// a tool-description test against the fake endpoints exercises the same words the AI would really see.</summary>
public static class AiResultInstructions
{
    private const string NothingChanged = "Nothing was booked or changed. Do NOT tell the patient an appointment was booked, confirmed or moved. ";
    private const string NothingCanceled = "Nothing was canceled. Do NOT tell the patient an appointment was canceled. ";

    public static string? ForSchedule(string code, string? existingLabel, string? requestedLabel) => code switch
    {
        "BOOKED" or "RESCHEDULED" => null,
        "CONFIRMATION_REQUIRED" => NothingChanged +
            $"The patient already has an appointment on {existingLabel}. Tell them so, and ask whether they want to MOVE it to {requestedLabel}. " +
            "Only if they clearly agree, call schedule_consultation again with the same scheduledStart and confirmReplaceExisting=true. " +
            "If they don't want to move it, do not call it again.",
        "MULTIPLE_UPCOMING_APPOINTMENTS" => NothingChanged +
            "The patient has several upcoming appointments (see existingUpcomingAppointments). Ask which one to move, then call schedule_consultation again " +
            "with confirmReplaceExisting=true and that appointment's id as appointmentId.",
        "SLOT_UNAVAILABLE" => NothingChanged +
            "Apologise briefly, call get_available_slots again, and offer the patient other times. Do not retry the same time.",
        _ => NothingChanged + "Tell the patient you couldn't complete this and that a team member will help shortly."
    };

    public static string? ForCancel(string code) => code switch
    {
        "CANCELED" => null,
        "NO_UPCOMING_APPOINTMENT" =>
            NothingCanceled + "Tell the patient you couldn't find an upcoming appointment to cancel (it may already be canceled). You may call get_my_appointments to check.",
        "MULTIPLE_UPCOMING_APPOINTMENTS" =>
            NothingCanceled + "The patient has several upcoming appointments (see existingUpcomingAppointments). Ask which one to cancel, confirm it, then call cancel_consultation again with that appointment's id as appointmentId.",
        _ => NothingCanceled + "Tell the patient you couldn't cancel it and that a team member will help shortly."
    };
}
