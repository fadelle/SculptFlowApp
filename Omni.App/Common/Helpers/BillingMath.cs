namespace Omni.App.Common.Helpers;

public static class BillingMath
{
    /// <summary>Money is stored with 6 decimals, rounded half away from zero.</summary>
    public static decimal RoundMoney(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    /// <summary>True when an amount a person types (a top-up, an adjustment) is in whole cents. Usage keeps 6 decimals
    /// internally; money put in or taken out by hand never does (QA t2-billing-23: 0.1234567 was stored as 0.123457).</summary>
    public static bool IsCents(decimal value) => decimal.Round(value, 2) == value;

    public static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
