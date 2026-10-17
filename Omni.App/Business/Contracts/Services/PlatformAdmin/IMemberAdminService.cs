using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Responses.Billing;

namespace Omni.App.Business.Contracts.Services.PlatformAdmin;

/// <summary>Client and member users (and their client memberships) across clients, for the admin portal. Admin users
/// are not here: see IAdminAccountService.</summary>
public interface IMemberAdminService
{
    Task<PagedResponse<MemberRow>> ListAsync(Guid? clientId, string? search, int page, CancellationToken ct);

    Task<MemberRow?> GetAsync(string userId, CancellationToken ct);

    /// <summary>Throws InvalidOperationException when the user has no membership, or another one is already active.</summary>
    Task<PlatformAdminChange> SetMembershipActiveAsync(string userId, bool active, CancellationToken ct);

    /// <summary>Locking also signs the user out everywhere (new security stamp).</summary>
    Task<PlatformAdminChange> SetLoginLockedAsync(string userId, bool locked, CancellationToken ct);

    /// <summary>Validated by the app's password rules (ArgumentException with the reasons); signs the user out everywhere.</summary>
    Task<PlatformAdminChange> SetPasswordAsync(string userId, string password, CancellationToken ct);

    Task<PlatformAdminChange> SetEmailConfirmedAsync(string userId, bool confirmed, CancellationToken ct);
}
