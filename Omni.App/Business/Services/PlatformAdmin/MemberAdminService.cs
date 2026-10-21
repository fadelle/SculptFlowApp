using Microsoft.AspNetCore.Identity;
using Omni.App.Business.Contracts.Services.Accounts;
using Omni.App.Business.Contracts.Services.Members;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Common.Enums;
using Omni.App.Entities.Models;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Responses.Billing;
using Omni.App.Persistence.Contracts.PlatformAdmin;

namespace Omni.App.Business.Services.PlatformAdmin;

public class MemberAdminService : IMemberAdminService
{
    private readonly IMemberAdminRepository _repository;
    private readonly IMemberService _members;
    private readonly UserManager<AppUser> _users;
    private readonly IPasswordResetService _passwordReset;

    public MemberAdminService(IMemberAdminRepository repository, IMemberService members, UserManager<AppUser> users, IPasswordResetService passwordReset)
    {
        _repository = repository;
        _members = members;
        _users = users;
        _passwordReset = passwordReset;
    }

    public Task<PagedResponse<MemberRow>> ListAsync(Guid? clientId, string? search, int page, CancellationToken ct) =>
        _repository.ListAsync(clientId, search, page, ct);

    public Task<MemberRow?> GetAsync(string userId, CancellationToken ct) => _repository.GetAsync(userId, ct);

    public async Task<PlatformAdminChange> SetMembershipActiveAsync(string userId, bool active, CancellationToken ct)
    {
        var membership = await _repository.GetLatestMembershipAsync(userId, ct)
                         ?? throw new InvalidOperationException("This user has no client membership.");
        // Same rules as a client deactivating/reactivating its own member (one active membership per user, seats).
        await _members.SetMembershipActiveAsync(membership, active, ct);
        return new PlatformAdminChange(membership.ClientId);
    }

    public async Task<PlatformAdminChange> SetLoginLockedAsync(string userId, bool locked, CancellationToken ct)
    {
        var user = await FindAsync(userId);
        Check(await _users.SetLockoutEnabledAsync(user, true));
        Check(await _users.SetLockoutEndDateAsync(user, locked ? DateTimeOffset.MaxValue : null));
        Check(await _users.ResetAccessFailedCountAsync(user));
        if (locked) Check(await _users.UpdateSecurityStampAsync(user));
        return new PlatformAdminChange(await _repository.ClientOfAsync(userId, ct));
    }

    public async Task<PlatformAdminChange> SetPasswordAsync(string userId, string password, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Enter a new password.");
        var user = await FindAsync(userId);
        // Validate first, so a rejected password doesn't leave the account with none.
        foreach (var validator in _users.PasswordValidators) Check(await validator.ValidateAsync(_users, user, password));
        if (await _users.HasPasswordAsync(user)) Check(await _users.RemovePasswordAsync(user));
        Check(await _users.AddPasswordAsync(user, password)); // also renews the security stamp
        Check(await _users.ResetAccessFailedCountAsync(user));
        return new PlatformAdminChange(await _repository.ClientOfAsync(userId, ct));
    }

    public async Task<PlatformAdminChange> SetEmailConfirmedAsync(string userId, bool confirmed, CancellationToken ct)
    {
        var user = await FindAsync(userId);
        user.EmailConfirmed = confirmed;
        Check(await _users.UpdateAsync(user));
        return new PlatformAdminChange(await _repository.ClientOfAsync(userId, ct));
    }

    public async Task<PlatformAdminChange> SendPasswordResetEmailAsync(string userId, string requestBaseUrl, CancellationToken ct)
    {
        await _passwordReset.SendForUserAsync(userId, requestBaseUrl, ct);
        return new PlatformAdminChange(await _repository.ClientOfAsync(userId, ct));
    }

    /// <summary>A client or member user; admins are managed through /api/platform-admin/admins instead.</summary>
    private async Task<AppUser> FindAsync(string userId) =>
        await _users.FindByIdAsync(userId) is { } user && user.Role != UserRole.Admin
            ? user
            : throw new KeyNotFoundException("User not found.");

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new ArgumentException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
