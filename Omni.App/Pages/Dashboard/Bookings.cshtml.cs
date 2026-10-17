using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;

namespace Omni.App.Pages.Dashboard;

/// <summary>Page 3 — Bookings: a month calendar. Only the page shell; everything on it (the visible month's
/// bookings, the day drawer, creating a booking) is loaded and driven through the existing
/// /api/bookings API (wwwroot/js/bookings-calendar.js) — the SAME bookings table and services used
/// by the AI assistant's booking tools and the rest of the dashboard. Clicking a booking in the day drawer opens
/// the existing /dashboard/bookings/{id} detail page (BookingDetailModel) — reused as-is, not duplicated.</summary>
public class BookingsModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;

    public BookingsModel(ICurrentClientContext clientContext)
    {
        _clientContext = clientContext;
    }

    public bool ClientConfigured { get; private set; }

    /// <summary>For the "Client time | My time" switch (_TimeViewSwitch).</summary>
    public string ClientTimezone { get; private set; } = "UTC";

    public async Task OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        ClientConfigured = client is not null;
        if (client is not null && !string.IsNullOrWhiteSpace(client.Timezone)) ClientTimezone = client.Timezone;
    }
}
