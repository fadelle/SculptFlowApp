namespace PlasticSurgery.Common.Enums;

/// <summary>Allowed values for SubscriptionPlan.BillingPeriod.</summary>
public static class BillingPeriod
{
    public const string Month = "month";
    public const string Year = "year";

    public static bool IsValid(string? value) => value is Month or Year;

    /// <summary>The end of one period that starts at <paramref name="start"/>.</summary>
    public static DateTimeOffset Advance(DateTimeOffset start, string period) =>
        period == Year ? start.AddYears(1) : start.AddMonths(1);
}
