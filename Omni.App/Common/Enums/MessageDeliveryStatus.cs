namespace Omni.App.Common.Enums;

/// <summary>The delivery statuses a provider reports for a message the business sent (messages.delivery_status). A status_update
/// with anything else is refused (QA t8-07: a missing status cleared the real one, "banana" was stored).</summary>
public static class MessageDeliveryStatus
{
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Read = "read";
    public const string Failed = "failed";
    public const string Deleted = "deleted";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Sent, Delivered, Read, Failed, Deleted
    };
}
