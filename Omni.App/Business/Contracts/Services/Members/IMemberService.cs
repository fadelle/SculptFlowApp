using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Members;

namespace Omni.App.Business.Contracts.Services.Members;

/// <summary>
/// The people who can sign in to a client: its client user and the members that user invited (IInviteService). Every
/// caller works on the client of its own <see cref="UserAccess"/> (from CurrentClientContext), never one from the request.
/// Only a user with the <c>client</c> role may deactivate or reactivate members (UnauthorizedAccessException, 403,
/// otherwise). Rules: a user has at most one active membership; active client and member users and open invites of a
/// client count against its plan's <c>max_users</c> seats (EntitlementDeniedException, 422).
/// </summary>
public interface IMemberService
{
    /// <summary>Members of this client only: active first, then by name/email.</summary>
    Task<IReadOnlyList<MemberResponse>> ListAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Deactivates or reactivates a member of the actor's client (never the client user, never yourself).
    /// A deactivated member is signed out on their next request.</summary>
    Task SetActiveAsync(UserAccess actor, string userId, bool active, CancellationToken ct = default);

    /// <summary>The signed-in user's own dashboard language (en, ar, fr), or null to follow the client's default. Any client
    /// or member user may set their own; ArgumentException for an unknown language.</summary>
    Task SetOwnLanguageAsync(UserAccess actor, string? language, CancellationToken ct = default);

    /// <summary>The membership rules every activation goes through (also the platform-admin members API): one active
    /// membership per user, and seats. Saves.</summary>
    Task SetMembershipActiveAsync(ClientUser membership, bool active, CancellationToken ct = default);
}
