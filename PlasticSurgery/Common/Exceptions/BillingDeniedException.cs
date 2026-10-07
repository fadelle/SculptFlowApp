using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Common.Exceptions;

/// <summary>The clinic can't be charged for what it asked to do (not enough balance, no price configured...), so it
/// wasn't done. Derives from InvalidOperationException on purpose: every existing controller already maps that to
/// 422 with the message, and campaign recipients record it as their failure reason. Messages are clinic-facing and
/// provider-neutral.</summary>
public class BillingDeniedException : InvalidOperationException
{
    /// <summary>One of <see cref="UsageFailureReason"/>.</summary>
    public string Reason { get; }

    public BillingDeniedException(string reason, string message) : base(message)
    {
        Reason = reason;
    }

    public static BillingDeniedException For(string reason) => new(reason, reason switch
    {
        UsageFailureReason.InsufficientFunds =>
            "Not enough prepaid balance to send this message, so it wasn't sent. See Settings → Billing.",
        UsageFailureReason.RateNotFound or UsageFailureReason.CurrencyMismatch =>
            "Sending this message isn't priced for your account yet, so it wasn't sent. Please contact support.",
        _ => "This message couldn't be billed, so it wasn't sent. Please contact support."
    });
}
