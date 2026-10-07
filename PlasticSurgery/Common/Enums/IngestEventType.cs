namespace PlasticSurgery.Common.Enums;

public static class IngestEventType
{
    public const string CustomerMessage = "customer_message";
    public const string BusinessAppEcho = "business_app_echo";
    public const string AiMessage = "ai_message";
    public const string StatusUpdate = "status_update";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        CustomerMessage, BusinessAppEcho, AiMessage, StatusUpdate
    };
}
