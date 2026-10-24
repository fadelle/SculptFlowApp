using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Inbox;

/// <summary>Automatic replies (crm.auto_replies), one row per client and channel.</summary>
public interface IAutoReplyRepository
{
    /// <summary>The client's rows, read-only.</summary>
    Task<IReadOnlyList<AutoReply>> ListForClientReadOnlyAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>The client's row for a channel, read-only; null when it has none.</summary>
    Task<AutoReply?> GetReadOnlyAsync(Guid clientId, string channel, CancellationToken ct = default);

    /// <summary>The client's row for a channel, tracked so changes are written on the next save; null when it has none.</summary>
    Task<AutoReply?> GetAsync(Guid clientId, string channel, CancellationToken ct = default);

    void Add(AutoReply autoReply);
}
