using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Responses.Billing;

namespace PlasticSurgery.Business.Contracts.Services.PlatformAdmin;

/// <summary>Staff accounts (Identity users and their clinic memberships) across clinics, for the admin portal.</summary>
public interface IStaffAdminService
{
    Task<PagedResponse<StaffRow>> ListAsync(Guid? clinicId, string? search, int page, CancellationToken ct);

    Task<StaffRow?> GetAsync(string userId, CancellationToken ct);

    /// <summary>Throws InvalidOperationException when the user has no membership, or another one is already active.</summary>
    Task<PlatformAdminChange> SetMembershipActiveAsync(string userId, bool active, CancellationToken ct);

    /// <summary>Locking also signs the user out everywhere (new security stamp).</summary>
    Task<PlatformAdminChange> SetLoginLockedAsync(string userId, bool locked, CancellationToken ct);

    /// <summary>Validated by the app's password rules (ArgumentException with the reasons); signs the user out everywhere.</summary>
    Task<PlatformAdminChange> SetPasswordAsync(string userId, string password, CancellationToken ct);

    Task<PlatformAdminChange> SetEmailConfirmedAsync(string userId, bool confirmed, CancellationToken ct);
}
