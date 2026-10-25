using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Inbox;

/// <summary>Inbox settings rows (crm.inbox_settings), one per connected account (channel connection).</summary>
public interface IInboxSettingsRepository
{
    /// <summary>The client's rows as read-only copies (cached: read on every incoming contact message).</summary>
    Task<IReadOnlyList<InboxSettings>> ListForClientReadOnlyAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>The row of one of the client's accounts, tracked so changes are written on the next save; null when the
    /// account has none.</summary>
    Task<InboxSettings?> GetForAccountAsync(Guid clientId, Guid channelIntegrationId, CancellationToken ct = default);

    /// <summary>The client's rows that use <paramref name="botId"/>, tracked.</summary>
    Task<IReadOnlyList<InboxSettings>> ListForBotAsync(Guid clientId, Guid botId, CancellationToken ct = default);

    void Add(InboxSettings settings);
}
