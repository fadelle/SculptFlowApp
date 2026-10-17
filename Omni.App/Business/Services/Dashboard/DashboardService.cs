using Omni.App.Business.Contracts.Services.Dashboard;
using Omni.App.Entities.Dtos.Dashboard;
using Omni.App.Entities.Responses.Dashboard;
using Omni.App.Persistence.Contracts.Dashboard;

namespace Omni.App.Business.Services.Dashboard;

public class DashboardService : IDashboardService
{
    private readonly IDashboardRepository _dashboard;

    public DashboardService(IDashboardRepository dashboard)
    {
        _dashboard = dashboard;
    }

    public async Task<DashboardSummaryResponse> GetSummaryAsync(
        Guid clientId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var counts = await _dashboard.GetSummaryCountsAsync(clientId, from, to, ct);

        // Old-contact reactivation isn't built yet (Project 8) — wire this up once that project
        // exists and reactivation attempts are tracked (e.g. via an events.event_type filter).
        var oldContactsRecovered = 0;

        return new DashboardSummaryResponse(
            counts.NewInterestedPeople, counts.BookingsMade, counts.BookingsAttended,
            counts.OrdersMade, counts.Revenue, oldContactsRecovered
        );
    }

    public Task<(IReadOnlyList<DashboardContactRow> Items, int TotalCount)> GetContactsAsync(
        Guid clientId, string? stage, string? search, string? source, int skip, int take, CancellationToken ct = default) =>
        _dashboard.ListContactsAsync(clientId, stage, search, source, skip, take, ct);

    public Task<(IReadOnlyList<DashboardBookingRow> Items, int TotalCount)> GetBookingsAsync(
        Guid clientId, string? status, string? search, int skip, int take, CancellationToken ct = default) =>
        _dashboard.ListBookingsAsync(clientId, status, search, skip, take, ct);

    public Task<IReadOnlyList<DashboardServiceRow>> GetServiceStatsAsync(
        Guid clientId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) =>
        _dashboard.GetServiceStatsAsync(clientId, from, to, ct);

    public async Task<DashboardAttentionResponse> GetAttentionAsync(Guid clientId, CancellationToken ct = default)
    {
        var counts = await _dashboard.GetAttentionCountsAsync(clientId, DateTimeOffset.UtcNow, ct);
        return new DashboardAttentionResponse(counts.ConversationsNeedingHuman, counts.OverdueFollowups, counts.BookingsNeedingOutcome);
    }
}
