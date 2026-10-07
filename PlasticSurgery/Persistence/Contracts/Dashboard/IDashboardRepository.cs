using PlasticSurgery.Entities.Dtos.Dashboard;

namespace PlasticSurgery.Persistence.Contracts.Dashboard;

/// <summary>Read-only reporting queries behind the clinic dashboard. Date ranges are [from, to); either bound may be null.</summary>
public interface IDashboardRepository
{
    Task<DashboardSummaryCounts> GetSummaryCountsAsync(Guid clinicId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default);

    Task<(IReadOnlyList<DashboardLeadRow> Items, int TotalCount)> ListLeadsAsync(Guid clinicId, string? status, string? search,
        string? source, int skip, int take, CancellationToken ct = default);

    Task<(IReadOnlyList<DashboardAppointmentRow> Items, int TotalCount)> ListAppointmentsAsync(Guid clinicId, string? status,
        string? search, int skip, int take, CancellationToken ct = default);

    /// <summary>Per procedure, ordered by completed revenue (highest first).</summary>
    Task<IReadOnlyList<DashboardProcedureRow>> GetProcedureStatsAsync(Guid clinicId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default);

    Task<DashboardAttentionCounts> GetAttentionCountsAsync(Guid clinicId, DateTimeOffset now, CancellationToken ct = default);
}
