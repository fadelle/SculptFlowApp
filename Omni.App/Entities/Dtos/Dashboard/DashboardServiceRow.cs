namespace Omni.App.Entities.Dtos.Dashboard;

/// <summary>Page 4 — Surgeries & Money, sliced by service.</summary>
public record DashboardServiceRow(
    Guid ServiceId,
    string ServiceName,
    int LeadCount,
    int OrderCount,
    int CompletedCount,
    decimal CompletedRevenue
);
