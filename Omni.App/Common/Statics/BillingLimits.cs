namespace Omni.App.Common.Statics;

/// <summary>
/// Upper bounds for money typed by an admin (QA t2-billing-05: 999,999,999,999 and 1e12 overflowed numeric(18,6) with a 500,
/// and a plan priced 1,000,000,000,000,000 was accepted). Each is far above any real price; a value over it is a 400.
/// Amounts are checked after the cents rule (BillingMath.IsCents), so one that rounds to 0 is refused too.
/// </summary>
public static class BillingLimits
{
    /// <summary>One wallet top-up or manual adjustment (either sign).</summary>
    public const decimal MaxWalletAmount = 100_000m;
    /// <summary>A plan's price and its included usage credit, per period.</summary>
    public const decimal MaxPlanPrice = 1_000_000m;
    /// <summary>An add-on's price per unit per period.</summary>
    public const decimal MaxAddonPrice = 1_000_000m;
    /// <summary>A rate's client price or provider cost, per unit.</summary>
    public const decimal MaxRatePrice = 100_000m;
}
