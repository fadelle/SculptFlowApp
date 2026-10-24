namespace Omni.App.Entities.Dtos.Reports;

/// <summary>A campaign that started sending in the period, with how its recipients got on (each count is "reached at
/// least this far", so Read is included in Delivered).</summary>
public sealed record ReportCampaignRow(Guid Id, string Name, DateTimeOffset? StartedAt, int Recipients, int Sent, int Delivered, int Read,
    int Replied, int Failed);
