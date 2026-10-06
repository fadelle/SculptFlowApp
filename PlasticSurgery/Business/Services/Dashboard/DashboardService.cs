using PlasticSurgery.Business.Contracts.Services.Dashboard;
using PlasticSurgery.Entities.Dtos.Dashboard;
using PlasticSurgery.Entities.Responses.Dashboard;
using PlasticSurgery.Persistence.Contracts.Dashboard;

namespace PlasticSurgery.Business.Services.Dashboard;

public class DashboardService : IDashboardService
{
    private readonly IDashboardRepository _dashboard;

    public DashboardService(IDashboardRepository dashboard)
    {
        _dashboard = dashboard;
    }

    public async Task<DashboardSummaryResponse> GetSummaryAsync(
        Guid clinicId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var counts = await _dashboard.GetSummaryCountsAsync(clinicId, from, to, ct);

        // Old-lead reactivation isn't built yet (Project 8) — wire this up once that project
        // exists and reactivation attempts are tracked (e.g. via an events.event_type filter).
        var oldLeadsRecovered = 0;

        return new DashboardSummaryResponse(
            counts.NewInterestedPeople, counts.ConsultationsBooked, counts.ConsultationsAttended,
            counts.SurgeriesBooked, counts.Revenue, oldLeadsRecovered
        );
    }

    public Task<(IReadOnlyList<DashboardLeadRow> Items, int TotalCount)> GetLeadsAsync(
        Guid clinicId, string? status, string? search, string? source, int skip, int take, CancellationToken ct = default) =>
        _dashboard.ListLeadsAsync(clinicId, status, search, source, skip, take, ct);

    public Task<(IReadOnlyList<DashboardAppointmentRow> Items, int TotalCount)> GetAppointmentsAsync(
        Guid clinicId, string? status, string? search, int skip, int take, CancellationToken ct = default) =>
        _dashboard.ListAppointmentsAsync(clinicId, status, search, skip, take, ct);

    public Task<IReadOnlyList<DashboardProcedureRow>> GetProcedureStatsAsync(
        Guid clinicId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default) =>
        _dashboard.GetProcedureStatsAsync(clinicId, from, to, ct);

    public async Task<DashboardAttentionResponse> GetAttentionAsync(Guid clinicId, CancellationToken ct = default)
    {
        var counts = await _dashboard.GetAttentionCountsAsync(clinicId, DateTimeOffset.UtcNow, ct);
        return new DashboardAttentionResponse(counts.ConversationsNeedingStaff, counts.OverdueFollowups, counts.AppointmentsNeedingOutcome);
    }
}
