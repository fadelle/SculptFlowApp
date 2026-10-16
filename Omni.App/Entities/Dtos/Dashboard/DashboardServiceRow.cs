namespace Omni.App.Entities.Dtos.Dashboard;

/// <summary>Page 4 — Orders & Money, sliced by service.</summary>
public record DashboardServiceRow(
    Guid ServiceId,
    string ServiceName,
    int ContactCount,
    int OrderCount,
    int CompletedCount,
    decimal CompletedRevenue
);
