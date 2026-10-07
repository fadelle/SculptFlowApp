namespace PlasticSurgery.Common.Helpers;

public static class BillingMath
{
    /// <summary>Money is stored with 6 decimals, rounded half away from zero.</summary>
    public static decimal RoundMoney(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    public static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
