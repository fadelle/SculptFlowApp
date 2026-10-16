using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Entities.Requests.BusinessTypes;
using Omni.App.Entities.Responses.BusinessTypes;

namespace Omni.App.Business.Contracts.Services.BusinessTypes;

/// <summary>
/// Business types (core.business_types) and their pipeline stages (crm.pipeline_stages): the sign-up picker, the stages a
/// client's contacts can be in, admin editing, and moving a client to another type. Errors are thrown with strings-file
/// keys (businessTypes.errors.*): ArgumentException 400 for invalid input, KeyNotFoundException 404, DuplicateRecordException
/// 409 for a taken code, InvalidOperationException 422 when a rule says no (a stage still in use, the last active type).
/// Labels and names are in the request's language (ITextContext) unless a method says otherwise.
/// </summary>
public interface IBusinessTypeService
{
    /// <summary>Active types by sort order, for the sign-up picker.</summary>
    Task<IReadOnlyList<BusinessTypeOption>> ListOptionsAsync(CancellationToken ct = default);

    /// <summary>Whether a client may be created with this type (it exists and is active).</summary>
    Task<bool> IsActiveAsync(string? code, CancellationToken ct = default);

    /// <summary>The client's stages in pipeline order (empty when the client doesn't exist).</summary>
    Task<IReadOnlyList<PipelineStageOption>> ListStagesForClientAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Whether <paramref name="key"/> is a stage of the client's business type.</summary>
    Task<bool> IsStageOfClientAsync(Guid clientId, string? key, CancellationToken ct = default);

    // ---- Admin (platform-admin portal; English only)

    /// <summary>Every type, active or not, with its client count.</summary>
    Task<IReadOnlyList<BusinessTypeResponse>> ListAsync(CancellationToken ct = default);

    Task<BusinessTypeResponse> GetAsync(string code, CancellationToken ct = default);

    Task<BusinessTypeResponse> CreateAsync(SaveBusinessTypeRequest request, CancellationToken ct = default);

    /// <summary>Replaces names, words, AI profile, sort order and stages (see <see cref="SaveBusinessTypeRequest"/>).</summary>
    Task<BusinessTypeResponse> UpdateAsync(string code, SaveBusinessTypeRequest request, CancellationToken ct = default);

    /// <summary>An inactive type is hidden from sign-up; its clients keep working. The last active type can't be switched off.</summary>
    Task<BusinessTypeResponse> SetActiveAsync(string code, bool active, CancellationToken ct = default);

    /// <summary>What <see cref="ChangeClientTypeAsync"/> would do, without changing anything.</summary>
    Task<BusinessTypeChangePreview> PreviewClientChangeAsync(Guid clientId, string? toCode, CancellationToken ct = default);

    /// <summary>Moves the client to another active type in one transaction: every contact goes to the stage with the same key
    /// and category in the new type if there is one, else to the new type's first stage of the same category (a key the old
    /// type doesn't know goes to the first open stage). Changing to the current type changes nothing.</summary>
    Task<BusinessTypeChangePreview> ChangeClientTypeAsync(Guid clientId, string? toCode, CancellationToken ct = default);
}
