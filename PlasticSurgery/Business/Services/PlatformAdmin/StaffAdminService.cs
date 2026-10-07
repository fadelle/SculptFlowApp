using Microsoft.AspNetCore.Identity;
using PlasticSurgery.Business.Contracts.Services.PlatformAdmin;
using PlasticSurgery.Entities.Dtos.PlatformAdmin;
using PlasticSurgery.Entities.Responses.Billing;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.PlatformAdmin;

namespace PlasticSurgery.Business.Services.PlatformAdmin;

public class StaffAdminService : IStaffAdminService
{
    private readonly IStaffAdminRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<IdentityUser> _users;
    private readonly TimeProvider _time;

    public StaffAdminService(IStaffAdminRepository repository, IUnitOfWork unitOfWork, UserManager<IdentityUser> users, TimeProvider time)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _users = users;
        _time = time;
    }

    public Task<PagedResponse<StaffRow>> ListAsync(Guid? clinicId, string? search, int page, CancellationToken ct) =>
        _repository.ListAsync(clinicId, search, page, ct);

    public Task<StaffRow?> GetAsync(string userId, CancellationToken ct) => _repository.GetAsync(userId, ct);

    public async Task<PlatformAdminChange> SetMembershipActiveAsync(string userId, bool active, CancellationToken ct)
    {
        var membership = await _repository.GetLatestMembershipAsync(userId, ct)
                         ?? throw new InvalidOperationException("This user has no clinic membership.");
        if (membership.IsActive == active) return new PlatformAdminChange(membership.ClinicId);
        if (active && await _repository.HasOtherActiveMembershipAsync(userId, membership.Id, ct))
            throw new InvalidOperationException("This user already has another active clinic membership.");
        membership.IsActive = active;
        membership.UpdatedAt = _time.GetUtcNow();
        await _unitOfWork.SaveChangesAsync(ct);
        return new PlatformAdminChange(membership.ClinicId);
    }

    public async Task<PlatformAdminChange> SetLoginLockedAsync(string userId, bool locked, CancellationToken ct)
    {
        var user = await FindAsync(userId);
        Check(await _users.SetLockoutEnabledAsync(user, true));
        Check(await _users.SetLockoutEndDateAsync(user, locked ? DateTimeOffset.MaxValue : null));
        Check(await _users.ResetAccessFailedCountAsync(user));
        if (locked) Check(await _users.UpdateSecurityStampAsync(user));
        return new PlatformAdminChange(await _repository.ClinicOfAsync(userId, ct));
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
        return new PlatformAdminChange(await _repository.ClinicOfAsync(userId, ct));
    }

    public async Task<PlatformAdminChange> SetEmailConfirmedAsync(string userId, bool confirmed, CancellationToken ct)
    {
        var user = await FindAsync(userId);
        user.EmailConfirmed = confirmed;
        Check(await _users.UpdateAsync(user));
        return new PlatformAdminChange(await _repository.ClinicOfAsync(userId, ct));
    }

    private async Task<IdentityUser> FindAsync(string userId) =>
        await _users.FindByIdAsync(userId) ?? throw new KeyNotFoundException("User not found.");

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new ArgumentException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
