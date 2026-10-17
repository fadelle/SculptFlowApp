using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Models;

namespace Omni.App.Persistence.Contracts.Clients;

/// <summary>Clients and their memberships (client_users).</summary>
public interface IClientRepository
{
    /// <summary>Tracked, so changes are written on the next save.</summary>
    Task<Client?> GetAsync(Guid clientId, CancellationToken ct = default);

    Task<Client?> GetReadOnlyAsync(Guid clientId, CancellationToken ct = default);

    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);

    void Add(Client client);

    void AddMembership(ClientUser membership);

    /// <summary>What the user may use in the main app now (an active client/member user with an active membership), or
    /// null. Cached per user (Cache:MembershipsMinutes); saving the user or a membership clears it.</summary>
    Task<UserAccess?> GetUserAccessAsync(string userId, CancellationToken ct = default);

    /// <summary>Every membership of the client with its user's email, name, role and last sign-in.</summary>
    Task<IReadOnlyList<ClientMemberRow>> ListMembersAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Tracked: the user's membership of this client, or null.</summary>
    Task<ClientUser?> GetMembershipAsync(Guid clientId, string userId, CancellationToken ct = default);

    /// <summary>Whether the user has an active membership other than this one (a user may have one active membership).</summary>
    Task<bool> HasOtherActiveMembershipAsync(string userId, Guid exceptMembershipId, CancellationToken ct = default);

    /// <summary>Seats in use: active memberships of active client and member users (the max_users entitlement).</summary>
    Task<int> CountSeatsInUseAsync(Guid clientId, CancellationToken ct = default);

    Task<IReadOnlyList<ClientSummaryRow>> ListByNamePrefixAsync(string namePrefix, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListMemberUserIdsAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Deletes the client row; the schema's ON DELETE CASCADE removes all of its data.</summary>
    Task DeleteAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Every client's id and name, by name.</summary>
    Task<IReadOnlyList<ClientSummaryRow>> ListNamesAsync(CancellationToken ct = default);
}
