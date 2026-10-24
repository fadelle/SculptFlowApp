namespace Omni.App.Entities.Dtos.Reports;

/// <summary>Messages in one 15-minute UTC slot (small enough for every UTC offset), grouped into viewer-local days or
/// months by the service.</summary>
public sealed record ReportMessageBucket(DateTimeOffset SlotStart, int Received, int Sent);
