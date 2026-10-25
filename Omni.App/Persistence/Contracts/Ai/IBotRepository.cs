using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Ai;

/// <summary>AI bots (crm.bots) with their Knowledge Base sources (crm.bot_knowledge_sources).</summary>
public interface IBotRepository
{
    /// <summary>The client's bots with their sources, oldest first, as read-only copies (cached: read on every incoming
    /// message and AI tool call).</summary>
    Task<IReadOnlyList<Bot>> ListForClientReadOnlyAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>One of the client's bots with its sources, tracked so changes are written on the next save; null when the
    /// client has no such bot.</summary>
    Task<Bot?> GetAsync(Guid clientId, Guid botId, CancellationToken ct = default);

    void Add(Bot bot);

    void Remove(Bot bot);

    void RemoveSource(BotKnowledgeSource source);
}
