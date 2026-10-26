using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Ai;

/// <summary>The AI bot's conversation memory (crm.bot_memory). Not cached: it changes on every turn. A memory key is a live
/// conversation id, or a bot test session id when <c>test</c> is true.</summary>
public interface IBotMemoryRepository
{
    /// <summary>The newest <paramref name="take"/> exchanges of the conversation, oldest first.</summary>
    Task<IReadOnlyList<BotMemory>> ListRecentReadOnlyAsync(Guid key, bool test, int take, CancellationToken ct = default);

    void Add(BotMemory memory);

    /// <summary>Deletes all but the newest <paramref name="keep"/> exchanges of the conversation (set-based).</summary>
    Task<int> DeleteAllButNewestAsync(Guid key, bool test, int keep, CancellationToken ct = default);
}
