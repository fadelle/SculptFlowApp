using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Clients;

/// <summary>Clients and their staff memberships (client_users).</summary>
public interface IClientRepository
{
    /// <summary>Tracked, so changes are written on the next save.</summary>
    Task<Client?> GetAsync(Guid clientId, CancellationToken ct = default);

    Task<Client?> GetReadOnlyAsync(Guid clientId, CancellationToken ct = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);

    void Add(Client client);

    void AddMembership(ClientUser membership);

    /// <summary>The client of the user's active membership, or null. Read-only copy (cached).</summary>
    Task<Client?> GetClientOfActiveMemberAsync(string userId, CancellationToken ct = default);

    Task<Guid?> GetClientIdOfActiveMemberAsync(string userId, CancellationToken ct = default);

    /// <summary>Every membership of the client with the user's email and full-name claim.</summary>
    Task<IReadOnlyList<StaffMemberRow>> ListStaffAsync(Guid clientId, string fullNameClaimType, CancellationToken ct = default);

    Task<IReadOnlyList<ClientSummaryRow>> ListByNamePrefixAsync(string namePrefix, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListMemberUserIdsAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Deletes the client row; the schema's ON DELETE CASCADE removes all of its data.</summary>
    Task DeleteAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Every client's id and name, by name.</summary>
    Task<IReadOnlyList<ClientSummaryRow>> ListNamesAsync(CancellationToken ct = default);
}
