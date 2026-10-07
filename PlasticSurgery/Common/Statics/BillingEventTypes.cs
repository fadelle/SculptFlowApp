using System.Data;

namespace PlasticSurgery.Common.Statics;

/// <summary>event_type values the billing module writes to the events table.</summary>
public static class BillingEventTypes
{
    public const string SubscriptionStarted = "subscription_started";
    public const string SubscriptionPlanChanged = "subscription_plan_changed";
    public const string SubscriptionRenewed = "subscription_renewed";
    public const string SubscriptionPastDue = "subscription_past_due";
    public const string SubscriptionExpired = "subscription_expired";
    public const string SubscriptionCancelled = "subscription_cancelled";
    public const string SubscriptionCancelScheduled = "subscription_cancel_scheduled";
    public const string SubscriptionResumed = "subscription_resumed";
    public const string ProviderBillingChanged = "provider_billing_changed";
}
