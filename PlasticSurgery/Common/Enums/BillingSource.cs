namespace PlasticSurgery.Common.Enums;

public static class BillingSource
{
    /// <summary>Usage reported by a channel (WhatsApp send, delivery callback...).</summary>
    public const string Channel = "channel";
    /// <summary>A platform admin through the billing admin API.</summary>
    public const string Admin = "admin";
    /// <summary>The app itself (signup, subscription change requested in-app).</summary>
    public const string System = "system";
    /// <summary>The billing maintenance worker (renewals, expiry, stale reservations).</summary>
    public const string Worker = "worker";
}
