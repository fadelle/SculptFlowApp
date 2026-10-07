namespace PlasticSurgery.Common.Enums;

public static class CampaignStatus
{
    public const string Draft = "draft";
    public const string Scheduled = "scheduled";
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Draft, Scheduled, Running, Paused, Completed, Cancelled, Failed
    };
}
