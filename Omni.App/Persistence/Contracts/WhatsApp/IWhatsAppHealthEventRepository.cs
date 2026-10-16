using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.WhatsApp;

public interface IWhatsAppHealthEventRepository
{
    void Add(WhatsAppHealthEvent healthEvent);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<WhatsAppHealthEvent>> ListForClientAsync(Guid clientId, int skip, int take, CancellationToken ct = default);

    /// <summary>Was this exact event (same connection, type and time) already recorded? Webhooks retry.</summary>
    Task<bool> ExistsAsync(Guid channelIntegrationId, string eventType, DateTimeOffset occurredAt, CancellationToken ct = default);
}
