using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.BusinessTypes;

/// <summary>Business types (core.business_types) with their pipeline stages (crm.pipeline_stages). Every read returns the
/// stages in pipeline order.</summary>
public interface IBusinessTypeRepository
{
    /// <summary>Every type (active or not) by sort order, read-only (cached).</summary>
    Task<IReadOnlyList<BusinessType>> ListReadOnlyAsync(CancellationToken ct = default);

    /// <summary>One type from the cached list, or null.</summary>
    Task<BusinessType?> GetReadOnlyAsync(string code, CancellationToken ct = default);

    /// <summary>Tracked with its stages, so changes are written on the next save.</summary>
    Task<BusinessType?> GetAsync(string code, CancellationToken ct = default);

    Task<bool> ExistsAsync(string code, CancellationToken ct = default);

    void Add(BusinessType type);

    void RemoveStage(PipelineStage stage);

    /// <summary>How many clients use each type (types no client uses are left out).</summary>
    Task<IReadOnlyDictionary<string, int>> CountClientsByTypeAsync(CancellationToken ct = default);

    /// <summary>How many contacts of the type's clients are in each stage key (keys nobody is in are left out).</summary>
    Task<IReadOnlyDictionary<string, int>> CountLeadsByStageAsync(string code, CancellationToken ct = default);
}
