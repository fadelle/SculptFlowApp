using Microsoft.AspNetCore.Identity;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Persistence.Contracts.PlatformAdmin;

public interface IStaffAdminRepository
{
    Task<PagedResponse<StaffRow>> ListAsync(Guid? clientId, string? search, int page, CancellationToken ct = default);

    Task<StaffRow?> GetAsync(string userId, CancellationToken ct = default);

    /// <summary>Tracked: the user's most recently updated membership.</summary>
    Task<ClientUser?> GetLatestMembershipAsync(string userId, CancellationToken ct = default);

    Task<bool> HasOtherActiveMembershipAsync(string userId, Guid exceptMembershipId, CancellationToken ct = default);


    /// <summary>The client of the user's membership (an active one first).</summary>
    Task<Guid?> ClientOfAsync(string userId, CancellationToken ct = default);
}
