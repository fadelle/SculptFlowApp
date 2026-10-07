using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Controllers.Filters;
using PlasticSurgery.Entities.Dtos.Ai;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Appointments;

namespace PlasticSurgery.Controllers.Integrations;

/// <summary>
/// TEST-ONLY: deterministic canned responses for every schedule_consultation/cancel_consultation outcome, so an n8n workflow can
/// be pointed at these instead of the real tools and the AI agent's reaction to each code can be watched without having to set up
/// the exact appointment state (existing bookings, closed days, race conditions...) that would otherwise be needed to trigger it.
///
/// NEVER touches the database and NEVER takes clinicId/leadId — it can't, since nothing here is real. The response shapes and
/// instruction wording are byte-for-byte what the real endpoints (Controllers/Integrations/AiController.cs) return for the same code, via the
/// shared AiResultInstructions helper, so a test here reflects what the AI would really see.
///
/// Same trust level as the real AI tool surface (RequireIngestKey) — this is still server-to-server n8n traffic, not something
/// worth exposing to the open internet, even though it's inert. Not linked from anywhere in the dashboard UI.
/// </summary>
[ApiController]
[Route("api/ai/test")]
[RequireIngestKey]
[ApiErrors]
public class AiTestController : ControllerBase
{
    private static readonly Guid FakeProcedureId = Guid.Parse("00000000-0000-0000-0000-0000000000bb");

    /// <summary>GET /api/ai/test/codes — lists every code this controller can return, and which endpoint simulates it.
    /// Call this first to see what's available.</summary>
    [HttpGet("codes")]
    public ActionResult<object> ListCodes() => Ok(new
    {
        schedule = new[] { "BOOKED", "RESCHEDULED", "CONFIRMATION_REQUIRED", "MULTIPLE_UPCOMING_APPOINTMENTS", "SLOT_UNAVAILABLE", "LEAD_NOT_FOUND", "INVALID_REQUEST" },
        cancel = new[] { "CANCELED", "NO_UPCOMING_APPOINTMENT", "MULTIPLE_UPCOMING_APPOINTMENTS", "INVALID_REQUEST" },
        usage = "POST /api/ai/test/schedule?code=<one of the schedule codes> or /api/ai/test/cancel?code=<one of the cancel codes>. " +
                "Same request body/headers as the real tool; clinicId/leadId in the URL are ignored. The response is exactly what " +
                "schedule_consultation/cancel_consultation would return for that code — point the n8n tool at this URL temporarily to test the AI's reaction."
    });

    /// <summary>POST /api/ai/test/schedule?code=CODE — returns the canned ScheduleConsultationResult for CODE. Body/other query
    /// params are accepted (so the same n8n tool node works unmodified) but ignored. Unknown/missing code → 400 listing the valid ones.</summary>
    [HttpPost("schedule")]
    public ActionResult<ScheduleConsultationResult> Schedule([FromQuery] string? code)
    {
        var target = Existing("Monday, Sep 28 at 3:00 PM", "2026-09-28", "15:00");
        var requestedLabel = "Tuesday, Sep 29 at 4:00 PM";

        ScheduleConsultationResult result;
        result = code?.ToUpperInvariant() switch
        {
        "BOOKED" => new ScheduleConsultationResult(true, "created", "BOOKED", "Appointment booked.",
            Appointment: Existing(requestedLabel, "2026-09-29", "16:00")),

        "RESCHEDULED" => new ScheduleConsultationResult(true, "rescheduled", "RESCHEDULED", "Appointment rescheduled.",
            Appointment: Existing(requestedLabel, "2026-09-29", "16:00"), PreviousAppointment: target),

        "CONFIRMATION_REQUIRED" => Fail("CONFIRMATION_REQUIRED",
            "Patient already has an upcoming appointment; nothing was changed.", new[] { target }, requestedLabel),

        "MULTIPLE_UPCOMING_APPOINTMENTS" => Fail("MULTIPLE_UPCOMING_APPOINTMENTS",
            "Patient has more than one upcoming appointment; it is unclear which to move.",
            new[] { target, Existing("Friday, Oct 2 at 10:00 AM", "2026-10-02", "10:00") }, requestedLabel),

        "SLOT_UNAVAILABLE" => Fail("SLOT_UNAVAILABLE", "That time is no longer available - it overlaps another appointment.", null, requestedLabel),

        "LEAD_NOT_FOUND" => Fail("LEAD_NOT_FOUND", "Lead not found for this clinic.", null, null),

        "INVALID_REQUEST" => Fail("INVALID_REQUEST", "That appointment is not one of this patient's current upcoming appointments.", new[] { target }, requestedLabel),

            null or "" => throw new ArgumentException("Pass ?code=. See GET /api/ai/test/codes for the valid schedule codes."),
            _ => throw new ArgumentException($"Unknown code '{code}'. See GET /api/ai/test/codes for the valid schedule codes.")
        };
        

        return Ok(result);

        ScheduleConsultationResult Fail(string c, string message, UpcomingAppointmentResponse[]? existing, string? label) =>
            new(false, "none", c, message, AiResultInstructions.ForSchedule(c, existing?.FirstOrDefault()?.Label, label), null, null, existing, label);
    }

    /// <summary>POST /api/ai/test/cancel?code=CODE — returns the canned CancelConsultationResult for CODE. Same convention as /schedule.</summary>
    [HttpPost("cancel")]
    public ActionResult<CancelConsultationResult> Cancel([FromQuery] string? code)
    {
        var target = Existing("Monday, Sep 28 at 11:00 AM", "2026-09-28", "11:00");

        CancelConsultationResult result;
        result = code?.ToUpperInvariant() switch
        {
        "CANCELED" => new CancelConsultationResult(true, "canceled", "CANCELED", "Appointment canceled.",
            Appointment: target with { Status = "canceled" }),

        "NO_UPCOMING_APPOINTMENT" => Fail("NO_UPCOMING_APPOINTMENT", "This patient has no upcoming appointment to cancel (it may already be canceled).", null),

        "MULTIPLE_UPCOMING_APPOINTMENTS" => Fail("MULTIPLE_UPCOMING_APPOINTMENTS",
            "Patient has more than one upcoming appointment; it is unclear which to cancel.",
            new[] { target, Existing("Friday, Oct 2 at 10:00 AM", "2026-10-02", "10:00") }),

        "INVALID_REQUEST" => Fail("INVALID_REQUEST", "That appointment is not one of this patient's current upcoming appointments.", new[] { target }),

            null or "" => throw new ArgumentException("Pass ?code=. See GET /api/ai/test/codes for the valid cancel codes."),
            _ => throw new ArgumentException($"Unknown code '{code}'. See GET /api/ai/test/codes for the valid cancel codes.")
        };
        

        return Ok(result);

        CancelConsultationResult Fail(string c, string message, UpcomingAppointmentResponse[]? existing) =>
            new(false, "none", c, message, AiResultInstructions.ForCancel(c), null, existing);
    }

    [NonAction]
    public static UpcomingAppointmentResponse Existing(string label, string date, string time) => new(
        Guid.NewGuid(), "booked", "consultation", FakeProcedureId, "Rhinoplasty",
        DateTimeOffset.Parse($"{date}T{time}:00+03:00"), DateTimeOffset.Parse($"{date}T{time}:00+03:00").AddMinutes(30),
        date, time, label);
}
