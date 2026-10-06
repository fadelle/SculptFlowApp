namespace PlasticSurgery.Common.Enums;

/// <summary>Free text at the DB level — not every Meta status maps cleanly to one of these, so
/// WhatsAppHealthService picks the closest fit rather than failing on anything unrecognized.</summary>
public static class WhatsAppHealthEventSeverity
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Critical = "critical";
}
