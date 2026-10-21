using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Responses.Billing;
using Omni.App.Entities.Responses.Members;

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

    /// <summary>Emails the user a password reset link (IPasswordResetService.SendForUserAsync, no hourly limit). Throws
    /// KeyNotFoundException for an unknown or admin user and EmailSendException (502) when the email can't be sent.</summary>
    Task<PlatformAdminChange> SendPasswordResetEmailAsync(string userId, string requestBaseUrl, CancellationToken ct);

    /// <summary>A client's invites that were neither accepted nor cancelled (IInviteService.ListAsync).</summary>
    Task<IReadOnlyList<InviteResponse>> ListInvitesAsync(Guid clientId, CancellationToken ct);

    /// <summary>Cancels any client's invite. KeyNotFoundException when unknown or already accepted.</summary>
    Task<PlatformAdminChange> RevokeInviteAsync(Guid inviteId, CancellationToken ct);
}
