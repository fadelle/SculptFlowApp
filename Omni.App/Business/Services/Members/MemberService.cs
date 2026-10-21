using Microsoft.AspNetCore.Identity;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Members;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Members;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Users;

namespace Omni.App.Business.Services.Members;

public class MemberService : IMemberService
{
    private readonly IClientRepository _clients;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<AppUser> _users;
    private readonly IEntitlementService _entitlements;
    private readonly IAccountTokenRepository _tokens;
    private readonly TimeProvider _time;

    public MemberService(IClientRepository clients, IUnitOfWork unitOfWork, UserManager<AppUser> users, IEntitlementService entitlements,
        IAccountTokenRepository tokens, TimeProvider time)
    {
        _clients = clients;
        _unitOfWork = unitOfWork;
        _users = users;
        _entitlements = entitlements;
        _tokens = tokens;
        _time = time;
    }

    public async Task<IReadOnlyList<MemberResponse>> ListAsync(Guid clientId, CancellationToken ct = default)
    {
        var rows = await _clients.ListMembersAsync(clientId, ct);

        return rows
            .OrderByDescending(r => r.IsActive)
            .ThenBy(r => r.FullName ?? r.Email, StringComparer.OrdinalIgnoreCase)
            .Select(ToResponse)
            .ToList();
    }

    public async Task SetActiveAsync(UserAccess actor, string userId, bool active, CancellationToken ct = default)
    {
        MemberRules.EnsureClientRole(actor);
        if (userId == actor.UserId) throw new ArgumentException("members.errors.notYourself");
        var membership = await _clients.GetMembershipAsync(actor.ClientId, userId, ct)
                         ?? throw new KeyNotFoundException("members.errors.notFound");
        var user = await _users.FindByIdAsync(userId) ?? throw new KeyNotFoundException("members.errors.notFound");
        // The client user signed the client up; only the platform admin changes that account.
        if (user.Role != UserRole.Member) throw new InvalidOperationException("members.errors.onlyMembers");

        await SetMembershipActiveAsync(membership, active, ct);
    }

    public async Task SetOwnLanguageAsync(UserAccess actor, string? language, CancellationToken ct = default)
    {
        var code = LanguageCode.Normalize(language);
        var user = await _users.FindByIdAsync(actor.UserId) ?? throw new KeyNotFoundException("members.errors.notFound");
        if (user.Language == code) return;
        user.Language = code;
        user.UpdatedAt = _time.GetUtcNow();
        // Saving the user clears its cached UserAccess, so the next request already shows the new language.
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task SetMembershipActiveAsync(ClientUser membership, bool active, CancellationToken ct = default)
    {
        if (membership.IsActive == active) return;
        if (active)
        {
            // One active membership per user: a user's current client is their active membership.
            if (await _clients.HasOtherActiveMembershipAsync(membership.UserId, membership.Id, ct))
                throw new InvalidOperationException("members.errors.otherClient");
            await EnsureSeatAsync(membership.ClientId, ct);
        }

        membership.IsActive = active;
        membership.UpdatedAt = _time.GetUtcNow();
        await _unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>One more active client/member user must fit the plan's seats (max_users; unlimited when unset). Open invites
    /// hold a seat too.</summary>
    private async Task EnsureSeatAsync(Guid clientId, CancellationToken ct)
    {
        var held = await _clients.CountSeatsInUseAsync(clientId, ct) + await _tokens.CountOpenInvitesAsync(clientId, _time.GetUtcNow(), null, ct);
        await _entitlements.EnsureWithinLimitAsync(clientId, EntitlementKeys.MaxUsers, held + 1, ct);
    }

    private static MemberResponse ToResponse(ClientMemberRow r) =>
        new(r.UserId, r.FullName, r.Email, r.Role, r.IsActive, r.CreatedAt, r.LastLoginAt);
}
