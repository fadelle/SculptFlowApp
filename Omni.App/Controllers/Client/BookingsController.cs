using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Bookings;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Requests.Bookings;
using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Controllers.Client;

[ApiController]
[Route("api/bookings")]
[RequireFeature(EntitlementKeys.Bookings)]
public class BookingsController : DashboardApiController
{
    private readonly IBookingService _bookings;
    private readonly IAvailabilityService _availability;

    public BookingsController(IBookingService bookings, IAvailabilityService availability, ICurrentClientContext clientContext) : base(clientContext)
    {
        _bookings = bookings;
        _availability = availability;
    }

    /// <summary>Open slots from the client's structured availability — same calculation the AI's get_available_slots uses.</summary>
    [HttpGet("available")]
    public async Task<ActionResult<AvailabilityResponse>> GetAvailable(
        [FromQuery] Guid? serviceId, [FromQuery] DateOnly? date, [FromQuery] int? days, CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        return Ok(await _availability.GetSlotsAsync(clientId.Value, serviceId, date, days ?? (date is null ? 7 : 1), ct));
    }

    [HttpPost]
    public async Task<ActionResult<BookingResponse>> Create([FromBody] CreateBookingRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var booking = await _bookings.CreateAsync(request with { ClientId = clientId.Value }, ct);
        return CreatedAtAction(nameof(List), null, booking);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookingResponse>> GetById(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var booking = await _bookings.GetByIdAsync(clientId.Value, id, ct);
        return booking is null ? NotFound() : Ok(booking);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<BookingResponse>> UpdateStatus(Guid id, [FromBody] UpdateBookingStatusRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var booking = await _bookings.UpdateStatusAsync(clientId.Value, id, request.Status, ct);
        return booking is null ? NotFound() : Ok(booking);
    }

    /// <summary>The Bookings page's month calendar. One call returns every booking in the visible grid
    /// (the requested month plus its leading/trailing days from adjacent months), grouped by local calendar day
    /// in the client's timezone, or the viewer's own (omni-tz cookie) with view=mine — see IBookingService.GetCalendarMonthAsync.</summary>
    [HttpGet("calendar")]
    public async Task<ActionResult<CalendarMonthResponse>> GetCalendarMonth(
        [FromQuery] int year, [FromQuery] int month, [FromQuery] string? view = null, CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        if (month is < 1 or > 12) return BadRequest(new { error = "month must be between 1 and 12." });
        if (year is < 1900 or > 3000) return BadRequest(new { error = "year is out of range." });

        var displayTz = view == "mine" ? ViewerTimeZone.Find(Request.Cookies[ViewerTimeZone.CookieName]) : null;
        var calendar = await _bookings.GetCalendarMonthAsync(clientId.Value, year, month, displayTz, ct);
        return Ok(calendar);
    }

    [HttpGet]
    public async Task<ActionResult<object>> List(
        [FromQuery] string? status,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 200);
        var (items, totalCount) = await _bookings.ListAsync(clientId.Value, status, from, to, skip, take, ct);
        return Ok(new { items, totalCount, skip, take });
    }
}
