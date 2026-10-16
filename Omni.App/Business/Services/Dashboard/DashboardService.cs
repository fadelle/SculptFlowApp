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

        // Old-lead reactivation isn't built yet (Project 8) — wire this up once that project
        // exists and reactivation attempts are tracked (e.g. via an events.event_type filter).
        var oldLeadsRecovered = 0;

        return new DashboardSummaryResponse(
            counts.NewInterestedPeople, counts.ConsultationsBooked, counts.ConsultationsAttended,
            counts.SurgeriesBooked, counts.Revenue, oldLeadsRecovered
        );
    }

    public Task<(IReadOnlyList<DashboardLeadRow> Items, int TotalCount)> GetLeadsAsync(
        Guid clientId, string? status, string? search, string? source, int skip, int take, CancellationToken ct = default) =>
        _dashboard.ListLeadsAsync(clientId, status, search, source, skip, take, ct);

    public Task<(IReadOnlyList<DashboardAppointmentRow> Items, int TotalCount)> GetAppointmentsAsync(
        Guid clientId, string? status, string? search, int skip, int take, CancellationToken ct = default) =>
        _dashboard.ListAppointmentsAsync(clientId, status, search, skip, take, ct);

    public Task<IReadOnlyList<DashboardProcedureRow>> GetProcedureStatsAsync(
        Guid clientId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) =>
        _dashboard.GetProcedureStatsAsync(clientId, from, to, ct);

    public async Task<DashboardAttentionResponse> GetAttentionAsync(Guid clientId, CancellationToken ct = default)
    {
        var counts = await _dashboard.GetAttentionCountsAsync(clientId, DateTimeOffset.UtcNow, ct);
        return new DashboardAttentionResponse(counts.ConversationsNeedingStaff, counts.OverdueFollowups, counts.AppointmentsNeedingOutcome);
    }
}
