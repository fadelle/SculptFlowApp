using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Bookings;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Bookings;

namespace Omni.App.Pages.Dashboard;

/// <summary>
/// client-facing edit page for Booking Status — posts through the same
/// IBookingService.UpdateStatusAsync the existing PATCH /api/bookings/{id}/status endpoint
/// already uses, so nothing here duplicates that logic. Matters most for "Old contacts who never
/// booked": marking a booking Booked/Confirmed/Attended here makes the contact stop matching the
/// reactivation audience on the very next count/preview, since ICampaignAudienceService always reads
/// live booking data — see its remarks.
/// </summary>
public class BookingDetailModel : PageModel
{
    private const string SavedKey = "bookingDetail.saved";

    private readonly ICurrentClientContext _clientContext;
    private readonly IBookingService _bookings;
    private readonly IStringLocalizer _text;
    private readonly IEntitlementService _entitlements;

    public BookingDetailModel(ICurrentClientContext clientContext, IBookingService bookings, IStringLocalizer text,
        IEntitlementService entitlements)
    {
        _entitlements = entitlements;
        _clientContext = clientContext;
        _bookings = bookings;
        _text = text;
    }

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public bool ClientConfigured { get; private set; }

    /// <summary>False when the plan has no bookings: the page shows that instead of the booking (status changes are refused too).</summary>
    public bool BookingsIncluded { get; private set; } = true;
    public BookingResponse? Booking { get; private set; }

    /// <summary>For the "Client time | My time" switch (_TimeViewSwitch); the header time defaults to client time.</summary>
    public string ClientTimezone { get; private set; } = "UTC";
    public IReadOnlyList<string> StatusOptions { get; } = BookingStatus.All.OrderBy(s => s).ToList();

    [BindProperty]
    public string Status { get; set; } = string.Empty;

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        if (ClientConfigured && BookingsIncluded && Booking is null) return NotFound();
        if (Booking is not null) Status = Booking.Status;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        try
        {
            var booking = await _bookings.UpdateStatusAsync(client.Id, Id, Status, ct);
            if (booking is null) return NotFound();

            StatusMessage = _text[SavedKey].Value;
            return RedirectToPage(new { Id });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = _text.Localize(ex);
            await LoadAsync(ct);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null)
        {
            ClientConfigured = false;
            return;
        }

        ClientConfigured = true;
        if (!string.IsNullOrWhiteSpace(client.Timezone)) ClientTimezone = client.Timezone;
        BookingsIncluded = (await _entitlements.GetAsync(client.Id, ct)).CanUseBookings;
        if (!BookingsIncluded) return;
        Booking = await _bookings.GetByIdAsync(client.Id, Id, ct);
    }
}
