namespace Omni.App.Business.Contracts.Engines.WhatsApp;

/// <summary>
/// The WhatsApp webhook's inbox (QA t10-10): the webhook stores a signed delivery and answers Meta at once
/// (<see cref="EnqueueAsync"/>); WhatsAppDeliveryWorker then processes deliveries one at a time, oldest first
/// (<see cref="ProcessNextAsync"/>): IMetaWebhookProcessor, then the AI triggers. A delivery that fails as a whole is
/// tried again later (up to <see cref="MaxAttempts"/>); one bad event inside a delivery never fails it (the processor
/// skips that event, QA t10-03).
/// </summary>
public interface IWhatsAppWebhookInbox
{
    public const int MaxAttempts = 5;

    /// <summary>Stores the raw signed body and wakes the worker. False when Meta redelivered a body already stored.</summary>
    Task<bool> EnqueueAsync(byte[] body, CancellationToken ct = default);

    /// <summary>Processes the oldest due delivery. False when nothing was due.</summary>
    Task<bool> ProcessNextAsync(CancellationToken ct = default);

    /// <summary>Deletes deliveries processed more than <paramref name="age"/> ago. Returns how many.</summary>
    Task<int> CleanupAsync(TimeSpan age, CancellationToken ct = default);
}

/// <summary>Wakes WhatsAppDeliveryWorker when a delivery is stored (a singleton; the worker also polls, so a missed signal or
/// another instance's delivery only waits a moment).</summary>
public interface IWhatsAppDeliverySignal
{
    void Notify();

    /// <summary>Waits for a notify or the timeout; true when notified.</summary>
    Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct);
}
