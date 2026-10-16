using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Inbox;

/// <summary>Inbox settings rows (crm.inbox_settings). Only the client-wide row (no channel connection) is used today.</summary>
public interface IInboxSettingsRepository
{
    /// <summary>The client-wide row, tracked so changes are written on the next save; null when the client has none.</summary>
    Task<InboxSettings?> GetForClientAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>The client-wide row as a read-only copy (cached, "none" too); null when the client has none.</summary>
    Task<InboxSettings?> GetForClientReadOnlyAsync(Guid clientId, CancellationToken ct = default);

    void Add(InboxSettings settings);
}
