namespace PlasticSurgery.Common.Enums;

/// <summary>Allowed values for ClinicSubscription.Status — must match schema.sql's CHECK constraint.</summary>
public static class SubscriptionStatus
{
    /// <summary>Paid for the current period.</summary>
    public const string Active = "active";
    /// <summary>The renewal couldn't be paid; the plan stays usable until the grace period (Billing:GracePeriodDays) ends.</summary>
    public const string PastDue = "past_due";
    /// <summary>The grace period ran out without payment. Data stays viewable; sending, campaigns and the AI stop.</summary>
    public const string Expired = "expired";
    /// <summary>Ended on request. Same restrictions as expired.</summary>
    public const string Cancelled = "cancelled";
}
