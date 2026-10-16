using Microsoft.AspNetCore.Mvc;
using Omni.App.Common.Statics;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Controllers.Integrations;

/// <summary>
/// TEST-ONLY: deterministic canned responses for every schedule_booking/cancel_booking outcome, so an n8n workflow can
/// be pointed at these instead of the real tools and the AI agent's reaction to each code can be watched without having to set up
/// the exact booking state (existing bookings, closed days, race conditions...) that would otherwise be needed to trigger it.
///
/// NEVER touches the database and NEVER takes clientId/contactId — it can't, since nothing here is real. The response shapes and
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
    private static readonly Guid FakeServiceId = Guid.Parse("00000000-0000-0000-0000-0000000000bb");

    /// <summary>GET /api/ai/test/codes — lists every code this controller can return, and which endpoint simulates it.
    /// Call this first to see what's available.</summary>
    [HttpGet("codes")]
    public ActionResult<object> ListCodes() => Ok(new
    {
        schedule = new[] { "BOOKED", "RESCHEDULED", "CONFIRMATION_REQUIRED", "MULTIPLE_UPCOMING_BOOKINGS", "SLOT_UNAVAILABLE", "CONTACT_NOT_FOUND", "INVALID_REQUEST" },
        cancel = new[] { "CANCELED", "NO_UPCOMING_BOOKING", "MULTIPLE_UPCOMING_BOOKINGS", "INVALID_REQUEST" },
        usage = "POST /api/ai/test/schedule?code=<one of the schedule codes> or /api/ai/test/cancel?code=<one of the cancel codes>. " +
                "Same request body/headers as the real tool; clientId/contactId in the URL are ignored. The response is exactly what " +
                "schedule_booking/cancel_booking would return for that code — point the n8n tool at this URL temporarily to test the AI's reaction."
    });

    /// <summary>POST /api/ai/test/schedule?code=CODE — returns the canned ScheduleBookingResult for CODE. Body/other query
    /// params are accepted (so the same n8n tool node works unmodified) but ignored. Unknown/missing code → 400 listing the valid ones.</summary>
    [HttpPost("schedule")]
    public ActionResult<ScheduleBookingResult> Schedule([FromQuery] string? code)
    {
        var target = Existing("Monday, Sep 28 at 3:00 PM", "2026-09-28", "15:00");
        var requestedLabel = "Tuesday, Sep 29 at 4:00 PM";

        ScheduleBookingResult result;
        result = code?.ToUpperInvariant() switch
        {
        "BOOKED" => new ScheduleBookingResult(true, "created", "BOOKED", "Booking made.",
            Booking: Existing(requestedLabel, "2026-09-29", "16:00")),

        "RESCHEDULED" => new ScheduleBookingResult(true, "rescheduled", "RESCHEDULED", "Booking rescheduled.",
            Booking: Existing(requestedLabel, "2026-09-29", "16:00"), PreviousBooking: target),

        "CONFIRMATION_REQUIRED" => Fail("CONFIRMATION_REQUIRED",
            "Contact already has an upcoming booking; nothing was changed.", new[] { target }, requestedLabel),

        "MULTIPLE_UPCOMING_BOOKINGS" => Fail("MULTIPLE_UPCOMING_BOOKINGS",
            "Contact has more than one upcoming booking; it is unclear which to move.",
            new[] { target, Existing("Friday, Oct 2 at 10:00 AM", "2026-10-02", "10:00") }, requestedLabel),

        "SLOT_UNAVAILABLE" => Fail("SLOT_UNAVAILABLE", "That time is no longer available - it overlaps another booking.", null, requestedLabel),

        "CONTACT_NOT_FOUND" => Fail("CONTACT_NOT_FOUND", "Contact not found for this client.", null, null),

        "INVALID_REQUEST" => Fail("INVALID_REQUEST", "That booking is not one of this contact's current upcoming bookings.", new[] { target }, requestedLabel),

            null or "" => throw new ArgumentException("Pass ?code=. See GET /api/ai/test/codes for the valid schedule codes."),
            _ => throw new ArgumentException($"Unknown code '{code}'. See GET /api/ai/test/codes for the valid schedule codes.")
        };
        

        return Ok(result);

        ScheduleBookingResult Fail(string c, string message, UpcomingBookingResponse[]? existing, string? label) =>
            new(false, "none", c, message, AiResultInstructions.ForSchedule(c, existing?.FirstOrDefault()?.Label, label), null, null, existing, label);
    }

    /// <summary>POST /api/ai/test/cancel?code=CODE — returns the canned CancelBookingResult for CODE. Same convention as /schedule.</summary>
    [HttpPost("cancel")]
    public ActionResult<CancelBookingResult> Cancel([FromQuery] string? code)
    {
        var target = Existing("Monday, Sep 28 at 11:00 AM", "2026-09-28", "11:00");

        CancelBookingResult result;
        result = code?.ToUpperInvariant() switch
        {
        "CANCELED" => new CancelBookingResult(true, "canceled", "CANCELED", "Booking canceled.",
            Booking: target with { Status = "canceled" }),

        "NO_UPCOMING_BOOKING" => Fail("NO_UPCOMING_BOOKING", "This contact has no upcoming booking to cancel (it may already be canceled).", null),

        "MULTIPLE_UPCOMING_BOOKINGS" => Fail("MULTIPLE_UPCOMING_BOOKINGS",
            "Contact has more than one upcoming booking; it is unclear which to cancel.",
            new[] { target, Existing("Friday, Oct 2 at 10:00 AM", "2026-10-02", "10:00") }),

        "INVALID_REQUEST" => Fail("INVALID_REQUEST", "That booking is not one of this contact's current upcoming bookings.", new[] { target }),

            null or "" => throw new ArgumentException("Pass ?code=. See GET /api/ai/test/codes for the valid cancel codes."),
            _ => throw new ArgumentException($"Unknown code '{code}'. See GET /api/ai/test/codes for the valid cancel codes.")
        };
        

        return Ok(result);

        CancelBookingResult Fail(string c, string message, UpcomingBookingResponse[]? existing) =>
            new(false, "none", c, message, AiResultInstructions.ForCancel(c), null, existing);
    }

    [NonAction]
    public static UpcomingBookingResponse Existing(string label, string date, string time) => new(
        Guid.NewGuid(), "booked", "standard", FakeServiceId, "Rhinoplasty",
        DateTimeOffset.Parse($"{date}T{time}:00+03:00"), DateTimeOffset.Parse($"{date}T{time}:00+03:00").AddMinutes(30),
        date, time, label);
}
