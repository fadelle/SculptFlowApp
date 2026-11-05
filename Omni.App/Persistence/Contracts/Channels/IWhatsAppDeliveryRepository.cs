using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Channels;

/// <summary>The WhatsApp webhook inbox (channels.whatsapp_webhook_deliveries; QA t10-10). Each method is one statement, so
/// it is safe with several app instances: a claim takes the oldest due delivery with FOR UPDATE SKIP LOCKED.</summary>
public interface IWhatsAppDeliveryRepository
{
    /// <summary>Stores a delivery; false when the same body (hash) is already stored (Meta redelivered it).</summary>
    Task<bool> EnqueueAsync(string body, string bodyHash, string? traceParent, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Claims the oldest due delivery (pending and due, or processing with a lapsed claim) until
    /// <paramref name="lockedUntil"/>, counting the attempt. Null when nothing is due.</summary>
    Task<WhatsAppWebhookDelivery?> ClaimNextAsync(DateTimeOffset now, DateTimeOffset lockedUntil, CancellationToken ct = default);

    Task CompleteAsync(long id, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Back to pending, tried again at <paramref name="retryAt"/>.</summary>
    Task RetryLaterAsync(long id, string error, DateTimeOffset retryAt, CancellationToken ct = default);

    /// <summary>Given up: kept as failed with its error for a person to look at.</summary>
    Task FailAsync(long id, string error, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Deletes done deliveries processed before <paramref name="before"/>. Returns how many.</summary>
    Task<int> DeleteDoneBeforeAsync(DateTimeOffset before, CancellationToken ct = default);
}
