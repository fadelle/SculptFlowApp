using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Knowledge;

/// <summary>Each client's Knowledge Base search settings (one row per client).</summary>
public interface IKnowledgeSettingsRepository
{
    Task<KnowledgeSearchSettings?> GetReadOnlyAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<KnowledgeSearchSettings?> GetAsync(Guid clientId, CancellationToken ct = default);

    void Add(KnowledgeSearchSettings settings);

    /// <summary>Stops tracking a row that wasn't saved (e.g. lost an insert race), leaving other pending changes alone.</summary>
    void Detach(KnowledgeSearchSettings settings);
}
