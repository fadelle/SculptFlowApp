using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Dtos.Members;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Members;
using Omni.App.Entities.Responses.Members;

namespace Omni.App.Business.Contracts.Services.Members;

/// <summary>
/// Member invites (identity.account_tokens, purpose invite): the client user invites someone by email; the invitee opens
/// the link, types a name and password and becomes a <c>member</c> of that client. Only the <c>client</c> role invites,
/// resends or cancels (UnauthorizedAccessException, 403). An open invite holds a seat (max_users), so seats in use =
/// active users + open invites. A link works once, for Accounts:InviteDays days; resending replaces it.
/// </summary>
public interface IInviteService
{
    /// <summary>Open and expired invites of the client (newest first); accepted and cancelled ones are gone.</summary>
    Task<IReadOnlyList<InviteResponse>> ListAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Stores the invite and emails the link in the client's language. ArgumentException for a bad email or a
    /// name over 200 characters, an email that already signs in (members.errors.emailTaken) or already has an open invite
    /// (members.errors.alreadyInvited); EntitlementDeniedException when no seat is free; EmailSendException when the email
    /// can't go (the invite is then not kept).</summary>
    Task<InviteResponse> InviteAsync(UserAccess actor, InviteMemberRequest request, string requestBaseUrl, CancellationToken ct = default);

    /// <summary>A new link for the same person (the old one stops working). Needs a free seat when the old link had
    /// expired. KeyNotFoundException for an unknown invite of this client.</summary>
    Task<InviteResponse> ResendAsync(UserAccess actor, Guid inviteId, string requestBaseUrl, CancellationToken ct = default);

    /// <summary>The link stops working and the seat is free again. KeyNotFoundException for an unknown invite of this
    /// client.</summary>
    Task RevokeAsync(UserAccess actor, Guid inviteId, CancellationToken ct = default);

    /// <summary>The admin portal's cancel: any client's invite. Returns its client. KeyNotFoundException when unknown.</summary>
    Task<Guid> RevokeAnyAsync(Guid inviteId, CancellationToken ct = default);

    /// <summary>The accept page for a link: null when it doesn't work (unknown, accepted, cancelled, expired, or its client
    /// is inactive).</summary>
    Task<InviteLanding?> GetLandingAsync(string? token, CancellationToken ct = default);

    /// <summary>Creates the member user (role member, email confirmed) and its active membership, marks the invite used, in
    /// one transaction, and returns the user to sign in. ArgumentException (acceptInvite.errors.*, Identity password rules,
    /// members.errors.emailTaken); InvalidOperationException (acceptInvite.errors.linkInvalid) for a dead link;
    /// EntitlementDeniedException when the plan lost the seat meanwhile.</summary>
    Task<AppUser> AcceptAsync(string? token, string? fullName, string? password, string? confirmPassword, CancellationToken ct = default);
}
