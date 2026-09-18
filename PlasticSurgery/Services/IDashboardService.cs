using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public interface IDashboardService
{
    Task<DashboardSummaryResponse> GetSummaryAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>search matches lead full name or phone (case-insensitive substring).</summary>
    Task<(IReadOnlyList<DashboardLeadRow> Items, int TotalCount)> GetLeadsAsync(
        Guid clinicId, string? status, string? search, int skip, int take, CancellationToken ct = default);

    /// <summary>search matches the appointment's lead full name or phone (case-insensitive substring).</summary>
    Task<(IReadOnlyList<DashboardAppointmentRow> Items, int TotalCount)> GetAppointmentsAsync(
        Guid clinicId, string? status, string? search, int skip, int take, CancellationToken ct = default);

    Task<IReadOnlyList<DashboardProcedureRow>> GetProcedureStatsAsync(Guid clinicId, CancellationToken ct = default);
}
