using PlasticSurgery.Entities.Dtos.Dashboard;
using PlasticSurgery.Entities.Responses.Dashboard;

namespace PlasticSurgery.Business.Contracts.Services.Dashboard;

public interface IDashboardService
{
    /// <summary>Funnel numbers for [from, to): leads created, appointments booked (created), appointments attended
    /// (scheduled in range), procedure bookings created, and revenue from bookings completed in range. Null bounds are open-ended.</summary>
    Task<DashboardSummaryResponse> GetSummaryAsync(Guid clinicId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default);

    Task<DashboardAttentionResponse> GetAttentionAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>search matches lead full name or phone (case-insensitive substring); source, when
    /// given, matches leads.source exactly (case-insensitive).</summary>
    Task<(IReadOnlyList<DashboardLeadRow> Items, int TotalCount)> GetLeadsAsync(
        Guid clinicId, string? status, string? search, string? source, int skip, int take, CancellationToken ct = default);

    /// <summary>search matches the appointment's lead full name or phone (case-insensitive substring).</summary>
    Task<(IReadOnlyList<DashboardAppointmentRow> Items, int TotalCount)> GetAppointmentsAsync(
        Guid clinicId, string? status, string? search, int skip, int take, CancellationToken ct = default);

    /// <summary>Per-procedure counts for [from, to), using the same date rules as GetSummaryAsync.</summary>
    Task<IReadOnlyList<DashboardProcedureRow>> GetProcedureStatsAsync(Guid clinicId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default);
}
