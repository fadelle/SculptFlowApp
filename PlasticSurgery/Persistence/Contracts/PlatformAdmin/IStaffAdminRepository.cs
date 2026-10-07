using Microsoft.AspNetCore.Identity;
using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Responses.Billing;

namespace PlasticSurgery.Persistence.Contracts.PlatformAdmin;

public interface IStaffAdminRepository
{
    Task<PagedResponse<StaffRow>> ListAsync(Guid? clinicId, string? search, int page, CancellationToken ct = default);

    Task<StaffRow?> GetAsync(string userId, CancellationToken ct = default);

    /// <summary>Tracked: the user's most recently updated membership.</summary>
    Task<ClinicUser?> GetLatestMembershipAsync(string userId, CancellationToken ct = default);

    Task<bool> HasOtherActiveMembershipAsync(string userId, Guid exceptMembershipId, CancellationToken ct = default);


    /// <summary>The clinic of the user's membership (an active one first).</summary>
    Task<Guid?> ClinicOfAsync(string userId, CancellationToken ct = default);
}
