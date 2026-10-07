namespace PlasticSurgery.Common.Enums;

public static class CampaignRecipientStatus
{
    /// <summary>Created (part of the audience snapshot) but the campaign hasn't started sending yet.</summary>
    public const string Pending = "pending";
    public const string Queued = "queued";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Read = "read";
    public const string Replied = "replied";
    public const string Booked = "booked";
    public const string Failed = "failed";
    /// <summary>Was part of the audience snapshot but deliberately not sent to — see SkipReason.</summary>
    public const string Skipped = "skipped";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Pending, Queued, Sent, Delivered, Read, Replied, Booked, Failed, Skipped
    };
}
