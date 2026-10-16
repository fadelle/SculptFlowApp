using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Catalog;

public interface IServiceRepository
{
    /// <summary>The client's services ordered by name. Read-only copies (cached).</summary>
    Task<IReadOnlyList<Service>> ListAsync(Guid clientId, bool activeOnly, CancellationToken ct = default);

    /// <summary>Tracked.</summary>
    Task<Service?> GetAsync(Guid clientId, Guid serviceId, CancellationToken ct = default);

    Task<Service?> GetReadOnlyAsync(Guid clientId, Guid serviceId, CancellationToken ct = default);

    Task<bool> IsActiveAsync(Guid serviceId, CancellationToken ct = default);

    /// <summary>Case-insensitive name match within the client, ignoring <paramref name="exceptId"/>.</summary>
    Task<bool> NameExistsAsync(Guid clientId, string name, Guid? exceptId, CancellationToken ct = default);

    void Add(Service service);
}
