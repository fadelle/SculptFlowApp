using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Persistence.Contracts.Procedures;

public interface IProcedureRepository
{
    /// <summary>The clinic's procedures ordered by name.</summary>
    Task<IReadOnlyList<Procedure>> ListAsync(Guid clinicId, bool activeOnly, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<Procedure?> GetAsync(Guid clinicId, Guid procedureId, CancellationToken ct = default);

    Task<Procedure?> GetReadOnlyAsync(Guid clinicId, Guid procedureId, CancellationToken ct = default);

    Task<bool> IsActiveAsync(Guid procedureId, CancellationToken ct = default);

    /// <summary>Case-insensitive name match within the clinic, ignoring <paramref name="exceptId"/>.</summary>
    Task<bool> NameExistsAsync(Guid clinicId, string name, Guid? exceptId, CancellationToken ct = default);

    void Add(Procedure procedure);
}
