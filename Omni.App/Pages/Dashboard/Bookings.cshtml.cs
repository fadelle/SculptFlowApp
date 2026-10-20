using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;

namespace Omni.App.Pages.Dashboard;

/// <summary>Page 3 — Bookings: a month calendar. Only the page shell; everything on it (the visible month's
/// bookings, the day drawer, creating a booking) is loaded and driven through the existing
/// /api/bookings API (wwwroot/js/bookings-calendar.js) — the SAME bookings table and services used
/// by the AI assistant's booking tools and the rest of the dashboard. Clicking a booking in the day drawer opens
/// the existing /dashboard/bookings/{id} detail page (BookingDetailModel) — reused as-is, not duplicated.</summary>
public class BookingsModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IEntitlementService _entitlements;

    public BookingsModel(ICurrentClientContext clientContext, IEntitlementService entitlements)
    {
        _clientContext = clientContext;
        _entitlements = entitlements;
    }

    public bool ClientConfigured { get; private set; }

    /// <summary>False when the plan has no bookings: the page shows that instead of the calendar (its API refuses too).</summary>
    public bool BookingsIncluded { get; private set; } = true;

    /// <summary>For the "Client time | My time" switch (_TimeViewSwitch).</summary>
    public string ClientTimezone { get; private set; } = "UTC";

    public async Task OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        ClientConfigured = client is not null;
        if (client is null) return;
        if (!string.IsNullOrWhiteSpace(client.Timezone)) ClientTimezone = client.Timezone;
        BookingsIncluded = (await _entitlements.GetAsync(client.Id, ct)).CanUseBookings;
    }
}
