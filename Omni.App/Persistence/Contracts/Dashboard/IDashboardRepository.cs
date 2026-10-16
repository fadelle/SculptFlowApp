using Omni.App.Entities.Dtos.Dashboard;

namespace Omni.App.Persistence.Contracts.Dashboard;

/// <summary>Read-only reporting queries behind the client dashboard. Date ranges are [from, to); either bound may be null.</summary>
public interface IDashboardRepository
{
    Task<DashboardSummaryCounts> GetSummaryCountsAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default);

    Task<(IReadOnlyList<DashboardContactRow> Items, int TotalCount)> ListContactsAsync(Guid clientId, string? stage, string? search,
        string? source, int skip, int take, CancellationToken ct = default);

    Task<(IReadOnlyList<DashboardBookingRow> Items, int TotalCount)> ListBookingsAsync(Guid clientId, string? status,
        string? search, int skip, int take, CancellationToken ct = default);

    /// <summary>Per service, ordered by completed revenue (highest first).</summary>
    Task<IReadOnlyList<DashboardServiceRow>> GetServiceStatsAsync(Guid clientId, DateTimeOffset? from, DateTimeOffset? to,
        CancellationToken ct = default);

    Task<DashboardAttentionCounts> GetAttentionCountsAsync(Guid clientId, DateTimeOffset now, CancellationToken ct = default);
}
