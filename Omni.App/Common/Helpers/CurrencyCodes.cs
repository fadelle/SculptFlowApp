using System.Globalization;

namespace Omni.App.Common.Helpers;

/// <summary>ISO 4217 currency codes, as .NET knows them from its regions (USD, EUR, LBP, AED...). Used wherever a person types a
/// currency (orders, service prices; QA t3-inbox-09), so "xyz" or "DOLLR" is refused instead of stored or failing the save.</summary>
public static class CurrencyCodes
{
    private static readonly Lazy<HashSet<string>> Known = new(() =>
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try { codes.Add(new RegionInfo(culture.Name).ISOCurrencySymbol); }
            catch (ArgumentException) { /* a culture without a region */ }
        }
        return codes;
    });

    /// <summary>The code upper-cased and trimmed when it is a known ISO 4217 code, else null.</summary>
    public static string? Normalize(string? code)
    {
        var clean = code?.Trim().ToUpperInvariant();
        return clean is { Length: 3 } && Known.Value.Contains(clean) ? clean : null;
    }
}
