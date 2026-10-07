namespace PlasticSurgery.Common.Enums;

/// <summary>The kind of value a setting holds; the settings API refuses values that don't parse as it.</summary>
public enum ConfigValueType
{
    String,
    Int,
    Decimal,
    Bool
}
