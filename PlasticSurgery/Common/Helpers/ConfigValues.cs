using System.Globalization;
using System.Text.RegularExpressions;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Dtos.Configuration;

namespace PlasticSurgery.Common.Helpers;

/// <summary>Parsing and checking setting values (invariant culture, so "0.5" means the same everywhere).</summary>
public static class ConfigValues
{
    /// <summary>Null when <paramref name="value"/> is valid for <paramref name="definition"/>, otherwise why it isn't.</summary>
    public static string? Validate(ConfigDefinition definition, string value)
    {
        switch (definition.Type)
        {
            case ConfigValueType.Int:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return "Enter a whole number.";
                return Range(definition, i);
            case ConfigValueType.Decimal:
                if (!decimal.TryParse(value, DecimalStyles, CultureInfo.InvariantCulture, out var d)) return "Enter a number (use . for decimals).";
                return Range(definition, d);
            case ConfigValueType.Bool:
                return bool.TryParse(value, out _) ? null : "Enter true or false.";
            default:
                return definition.Pattern is { } pattern && !Regex.IsMatch(value, pattern)
                    ? "That value doesn't have the expected format." : null;
        }
    }

    public static int ToInt(string value) => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    public static decimal ToDecimal(string value) => decimal.Parse(value, DecimalStyles, CultureInfo.InvariantCulture);

    public static bool ToBool(string value) => bool.Parse(value);

    // No thousands separators: "1,5" must not be read as 15.
    private const NumberStyles DecimalStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    private static string? Range(ConfigDefinition definition, decimal value)
    {
        if (definition.Min is { } min && value < min) return $"The value must be at least {min.ToString(CultureInfo.InvariantCulture)}.";
        if (definition.Max is { } max && value > max) return $"The value must be at most {max.ToString(CultureInfo.InvariantCulture)}.";
        return null;
    }
}
