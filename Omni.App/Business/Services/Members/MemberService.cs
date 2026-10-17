using Microsoft.AspNetCore.Identity;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Members;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Members;
using Omni.App.Entities.Responses.Members;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Clients;

namespace Omni.App.Business.Services.Members;

public class MemberService : IMemberService
{
    private const int MaxNameLength = 200;

    private readonly IClientRepository _clients;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<AppUser> _users;
    private readonly IEntitlementService _entitlements;
    private readonly TimeProvider _time;

    public MemberService(IClientRepository clients, IUnitOfWork unitOfWork, UserManager<AppUser> users, IEntitlementService entitlements,
        TimeProvider time)
    {
        _clients = clients;
        _unitOfWork = unitOfWork;
        _users = users;
        _entitlements = entitlements;
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

    public async Task<MemberResponse> AddAsync(UserAccess actor, AddMemberRequest request, CancellationToken ct = default)
    {
        EnsureClientRole(actor);
        var fullName = (request.FullName ?? string.Empty).Trim();
        var email = (request.Email ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;
        if (fullName.Length == 0) throw new ArgumentException("members.errors.nameRequired");
        if (fullName.Length > MaxNameLength) throw new ArgumentException("members.errors.nameTooLong").WithArgs(MaxNameLength);
        if (!EmailAddress.IsValid(email)) throw new ArgumentException("members.errors.emailInvalid");
        if (password.Length == 0) throw new ArgumentException("members.errors.passwordRequired");
        // Sign-in names are emails, so one address is one user in every app (admins included).
        if (await _users.FindByNameAsync(email) is not null || await _users.FindByEmailAsync(email) is not null)
            throw new ArgumentException("members.errors.emailTaken");

        await EnsureSeatAsync(actor.ClientId, ct);

        var now = _time.GetUtcNow();
        var user = new AppUser
        {
            UserName = email, Email = email, FullName = fullName, Role = UserRole.Member, IsActive = true,
            CreatedAt = now, UpdatedAt = now
        };
        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        var created = await _users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            await tx.RollbackAsync(ct);
            _unitOfWork.DiscardChanges();
            throw new ArgumentException(string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        _clients.AddMembership(new ClientUser
        {
            Id = Guid.NewGuid(), ClientId = actor.ClientId, UserId = user.Id, IsActive = true, CreatedAt = now, UpdatedAt = now
        });
        await _unitOfWork.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToResponse(new ClientMemberRow(user.Id, user.Email, user.FullName, user.Role, true, now, null));
    }

    public async Task SetActiveAsync(UserAccess actor, string userId, bool active, CancellationToken ct = default)
    {
        EnsureClientRole(actor);
        if (userId == actor.UserId) throw new ArgumentException("members.errors.notYourself");
        var membership = await _clients.GetMembershipAsync(actor.ClientId, userId, ct)
                         ?? throw new KeyNotFoundException("members.errors.notFound");
        var user = await _users.FindByIdAsync(userId) ?? throw new KeyNotFoundException("members.errors.notFound");
        // The client user signed the client up; only the platform admin changes that account.
        if (user.Role != UserRole.Member) throw new InvalidOperationException("members.errors.onlyMembers");

        await SetMembershipActiveAsync(membership, active, ct);
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

    /// <summary>One more active client/member user must fit the plan's seats (max_users; unlimited when unset).</summary>
    private async Task EnsureSeatAsync(Guid clientId, CancellationToken ct) =>
        await _entitlements.EnsureWithinLimitAsync(clientId, EntitlementKeys.MaxUsers, await _clients.CountSeatsInUseAsync(clientId, ct) + 1, ct);

    private static void EnsureClientRole(UserAccess actor)
    {
        if (actor.Role != UserRole.Client) throw new UnauthorizedAccessException("members.errors.clientOnly");
    }

    private static MemberResponse ToResponse(ClientMemberRow r) =>
        new(r.UserId, r.FullName, r.Email, r.Role, r.IsActive, r.CreatedAt, r.LastLoginAt);
}
