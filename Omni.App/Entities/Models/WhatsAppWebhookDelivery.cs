namespace Omni.App.Entities.Models;

/// <summary>One signed WhatsApp webhook delivery waiting for (or done with) background processing
/// (channels.whatsapp_webhook_deliveries; QA t10-10). The webhook stores it and answers Meta at once;
/// WhatsAppDeliveryWorker processes deliveries in order.</summary>
public class WhatsAppWebhookDelivery
{
    public long Id { get; set; }
    /// <summary>The raw body exactly as Meta signed it.</summary>
    public string Body { get; set; } = string.Empty;
    /// <summary>SHA-256 of the body (lowercase hex): a redelivered identical body is stored once.</summary>
    public string BodyHash { get; set; } = string.Empty;
    /// <summary>The webhook request's trace, which processing continues.</summary>
    public string? TraceParent { get; set; }
    public string Status { get; set; } = WhatsAppDeliveryStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    /// <summary>While processing: when the claim lapses, so a crashed worker's delivery is picked up again.</summary>
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? LastError { get; set; }
}

public static class WhatsAppDeliveryStatus
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Done = "done";
    public const string Failed = "failed";
}
