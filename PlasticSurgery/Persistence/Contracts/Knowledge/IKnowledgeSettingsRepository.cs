using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Knowledge;

/// <summary>Each clinic's Knowledge Base search settings (one row per clinic).</summary>
public interface IKnowledgeSettingsRepository
{
    Task<KnowledgeSearchSettings?> GetReadOnlyAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<KnowledgeSearchSettings?> GetAsync(Guid clinicId, CancellationToken ct = default);

    void Add(KnowledgeSearchSettings settings);

    /// <summary>Stops tracking a row that wasn't saved (e.g. lost an insert race), leaving other pending changes alone.</summary>
    void Detach(KnowledgeSearchSettings settings);
}
