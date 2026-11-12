using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Channels;

/// <summary>A client's messaging channel connections (one row per client and channel). The ReadOnly reads, GetIdAsync and
/// CountConnectedExceptAsync answer from the cache (CacheKeys.ChannelIntegrations / ChannelIntegration).</summary>
public interface IChannelIntegrationRepository
{
    void Add(ChannelIntegration integration);

    /// <summary>Tracked.</summary>
    Task<ChannelIntegration?> GetAsync(Guid clientId, string channel, CancellationToken ct = default);

    Task<ChannelIntegration?> GetReadOnlyAsync(Guid clientId, string channel, CancellationToken ct = default);

    /// <summary>A send found the connection broken (QA t8-10): health "problem" with the code and Meta's message (also LastError),
    /// set-based (the failing send's own changes are untouched), then the cached rows are dropped. True when it moved into
    /// a problem now (it wasn't already a problem or disconnected), so the team is told once.</summary>
    Task<bool> MarkSendProblemAsync(ChannelIntegration integration, string code, string message, DateTimeOffset at, CancellationToken ct = default);

    Task<ChannelIntegration?> GetByIdReadOnlyAsync(Guid integrationId, CancellationToken ct = default);

    /// <summary>Tracked, all channels of the client.</summary>
    Task<IReadOnlyList<ChannelIntegration>> ListForClientAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>The id of the client's row for the channel, or Guid.Empty.</summary>
    Task<Guid> GetIdAsync(Guid clientId, string channel, CancellationToken ct = default);

    /// <summary>The CONNECTED WhatsApp rows with this Meta phone number id, read-only (webhook routing, QA t10-01: a
    /// disconnected row never receives anything).</summary>
    Task<IReadOnlyList<ChannelIntegration>> ListConnectedWhatsAppByPhoneNumberIdAsync(string phoneNumberId, CancellationToken ct = default);

    /// <summary>The CONNECTED WhatsApp rows with this WhatsApp Business Account id, read-only (webhook routing of events that
    /// name only the account; more than one is ambiguous and nothing is routed).</summary>
    Task<IReadOnlyList<ChannelIntegration>> ListConnectedWhatsAppByWabaIdAsync(string wabaId, CancellationToken ct = default);

    /// <summary>Is this Telegram bot connected to a different client?</summary>
    Task<bool> IsTelegramBotConnectedElsewhereAsync(string botId, Guid clientId, CancellationToken ct = default);

    /// <summary>Connected channels of the client other than <paramref name="exceptChannel"/>.</summary>
    Task<int> CountConnectedExceptAsync(Guid clientId, string exceptChannel, CancellationToken ct = default);

    /// <summary>Stamps LastWebhookAt directly in the database.</summary>
    Task TouchLastWebhookAsync(Guid integrationId, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Read-only, all channels of the client, by channel.</summary>
    Task<IReadOnlyList<ChannelIntegration>> ListForClientReadOnlyAsync(Guid clientId, CancellationToken ct = default);
}
