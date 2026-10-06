namespace PlasticSurgery.Entities.Dtos.Dashboard;

/// <summary>Page 4 — Surgeries & Money, sliced by procedure.</summary>
public record DashboardProcedureRow(
    Guid ProcedureId,
    string ProcedureName,
    int LeadCount,
    int BookingCount,
    int CompletedCount,
    decimal CompletedRevenue
);
