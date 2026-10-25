using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Ai;

/// <summary>Bot test conversations (crm.bot_test_sessions, crm.bot_test_messages). Not cached: they change on every turn.</summary>
public interface IBotTestRepository
{
    /// <summary>One of the client's test sessions, tracked; null when it doesn't exist (or is another client's).</summary>
    Task<BotTestSession?> GetSessionAsync(Guid clientId, Guid sessionId, CancellationToken ct = default);

    /// <summary>Locks one of the client's test sessions FOR UPDATE until the transaction ends and returns it tracked (so two
    /// handoff calls for the same test can't both act); null when it doesn't exist.</summary>
    Task<BotTestSession?> GetSessionForUpdateAsync(Guid clientId, Guid sessionId, CancellationToken ct = default);

    /// <summary>A session's lines in order, oldest first.</summary>
    Task<IReadOnlyList<BotTestMessage>> ListMessagesAsync(Guid sessionId, CancellationToken ct = default);

    void AddSession(BotTestSession session);

    void AddMessage(BotTestMessage message);

    /// <summary>Set-based delete of every session (and its lines) last changed before <paramref name="before"/>, any client.</summary>
    Task<int> DeleteSessionsOlderThanAsync(DateTimeOffset before, CancellationToken ct = default);

    /// <summary>Set-based delete of one of the client's sessions and its lines.</summary>
    Task<int> DeleteSessionAsync(Guid clientId, Guid sessionId, CancellationToken ct = default);
}
