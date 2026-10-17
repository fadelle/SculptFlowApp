namespace Omni.App.Common.Enums;

public static class BenchmarkGenerationStatus
{
    public const string Pending = "pending";
    public const string Completed = "completed";
    public const string Failed = "failed";
    /// <summary>stopped by a member while still waiting for n8n; n8n's late reply is refused.</summary>
    public const string Cancelled = "cancelled";
}
