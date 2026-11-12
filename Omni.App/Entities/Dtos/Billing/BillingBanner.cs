namespace Omni.App.Entities.Dtos.Billing;

/// <summary>The billing line a dashboard page shows (IBillingBannerService; QA t2-billing-12). <see cref="TextKey"/> takes
/// <see cref="When"/> (a date shown in the viewer's time) or the balance (<see cref="Amount"/> in <see cref="Currency"/>).</summary>
public sealed record BillingBanner(string Kind, string TextKey, DateTimeOffset? When, decimal? Amount, string? Currency)
{
    public const string None = "none";
    public const string Ended = "ended";
    public const string PastDue = "past_due";
    public const string TrialEnding = "trial_ending";
    public const string LowBalance = "low_balance";

    /// <summary>Urgent ones (sending has stopped or is about to) are shown as errors, the rest as warnings.</summary>
    public bool Urgent => Kind is Ended or PastDue;
}
