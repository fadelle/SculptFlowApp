namespace Omni.App.Entities.Dtos.Reports;

/// <summary>Bookings made (created) in the period and, by scheduled start in the period, how they turned out.</summary>
public sealed record ReportBookingCounts(int Made, int Attended, int NoShow, int Canceled);
