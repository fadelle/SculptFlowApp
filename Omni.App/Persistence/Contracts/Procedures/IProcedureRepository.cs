using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Procedures;

public interface IProcedureRepository
{
    /// <summary>The client's procedures ordered by name. Read-only copies (cached).</summary>
    Task<IReadOnlyList<Procedure>> ListAsync(Guid clientId, bool activeOnly, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<Procedure?> GetAsync(Guid clientId, Guid procedureId, CancellationToken ct = default);

    Task<Procedure?> GetReadOnlyAsync(Guid clientId, Guid procedureId, CancellationToken ct = default);

    Task<bool> IsActiveAsync(Guid procedureId, CancellationToken ct = default);

    /// <summary>Case-insensitive name match within the client, ignoring <paramref name="exceptId"/>.</summary>
    Task<bool> NameExistsAsync(Guid clientId, string name, Guid? exceptId, CancellationToken ct = default);

    void Add(Procedure procedure);
}
