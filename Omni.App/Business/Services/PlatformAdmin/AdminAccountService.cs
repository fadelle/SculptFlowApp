using Microsoft.AspNetCore.Identity;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contracts.PlatformAdmin;

namespace Omni.App.Business.Services.PlatformAdmin;

public class AdminAccountService : IAdminAccountService
{
    private const int MaxNameLength = 200;

    private readonly UserManager<AppUser> _users;
    private readonly IAdminUserRepository _admins;
    private readonly TimeProvider _time;

    public AdminAccountService(UserManager<AppUser> users, IAdminUserRepository admins, TimeProvider time)
    {
        _users = users;
        _admins = admins;
        _time = time;
    }

    public async Task<AdminIdentity?> SignInAsync(string? email, string? password, CancellationToken ct = default)
    {
        email = (email ?? string.Empty).Trim();
        password ??= string.Empty;
        var user = email.Length == 0 ? null : await _users.FindByEmailAsync(email);
        if (user is null || user.Role != UserRole.Admin)
        {
            // Hash anyway, so an unknown email takes as long as a wrong password.
            _users.PasswordHasher.HashPassword(new AppUser(), password);
            return null;
        }

        if (await _users.IsLockedOutAsync(user) || !user.IsActive) return null;
        // CheckPasswordAsync also rehashes an old-format hash on success.
        if (!await _users.CheckPasswordAsync(user, password))
        {
            await _users.AccessFailedAsync(user); // locks after AccountRules.MaxFailedSignIns
            return null;
        }

        await _users.ResetAccessFailedCountAsync(user);
        user.LastLoginAt = _time.GetUtcNow();
        user.UpdatedAt = user.LastLoginAt.Value;
        await _users.UpdateAsync(user);
        return ToIdentity(user);
    }

    public async Task<AdminIdentity?> GetActiveAsync(string id, CancellationToken ct = default) =>
        await _users.FindByIdAsync(id) is { Role: UserRole.Admin, IsActive: true } user ? ToIdentity(user) : null;

    public Task<IReadOnlyList<AdminRow>> ListAsync(CancellationToken ct = default) => _admins.ListAsync(ct);

    public async Task<AdminRow> CreateAsync(string? email, string? fullName, string? password, CancellationToken ct = default)
    {
        email = (email ?? string.Empty).Trim();
        fullName = (fullName ?? string.Empty).Trim();
        if (!EmailAddress.IsValid(email)) throw new ArgumentException("Enter a valid email.");
        if (fullName.Length == 0) throw new ArgumentException("Enter a name.");
        if (fullName.Length > MaxNameLength) throw new ArgumentException($"The name can be at most {MaxNameLength} characters.");
        CheckPasswordLength(password);
        // One address is one user in every app, so a client or member email can't become an admin too.
        if (await _users.FindByNameAsync(email) is not null || await _users.FindByEmailAsync(email) is not null)
            throw new ArgumentException("A user with this email already exists.");

        var now = _time.GetUtcNow();
        var user = new AppUser
        {
            UserName = email, Email = email, EmailConfirmed = true, FullName = fullName, Role = UserRole.Admin, IsActive = true,
            CreatedAt = now, UpdatedAt = now
        };
        Check(await _users.CreateAsync(user, password!));
        return ToRow(user);
    }

    public async Task<AdminRow> SetActiveAsync(string id, bool active, string? actingAdminId, CancellationToken ct = default)
    {
        var user = await FindAdminAsync(id);
        if (user.IsActive == active) return ToRow(user);
        if (!active)
        {
            if (id == actingAdminId) throw new ArgumentException("You can't deactivate your own account.");
            if (await _admins.CountActiveAsync(ct) <= 1) throw new ArgumentException("At least one admin must stay active.");
        }

        user.IsActive = active;
        user.UpdatedAt = _time.GetUtcNow();
        Check(await _users.UpdateAsync(user));
        return ToRow(user);
    }

    public async Task<AdminRow> SetPasswordAsync(string id, string? password, CancellationToken ct = default)
    {
        CheckPasswordLength(password);
        var user = await FindAdminAsync(id);
        // Validate first, so a refused password doesn't leave the account with none.
        foreach (var validator in _users.PasswordValidators) Check(await validator.ValidateAsync(_users, user, password));
        if (await _users.HasPasswordAsync(user)) Check(await _users.RemovePasswordAsync(user));
        Check(await _users.AddPasswordAsync(user, password!));
        Check(await _users.ResetAccessFailedCountAsync(user));
        Check(await _users.SetLockoutEndDateAsync(user, null));
        return ToRow(user);
    }

    private async Task<AppUser> FindAdminAsync(string id) =>
        await _users.FindByIdAsync(id) is { Role: UserRole.Admin } user ? user : throw new KeyNotFoundException("Admin not found.");

    private static void CheckPasswordLength(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < AccountRules.AdminMinPasswordLength)
            throw new ArgumentException($"Password must be at least {AccountRules.AdminMinPasswordLength} characters.");
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new ArgumentException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    private static AdminIdentity ToIdentity(AppUser user) => new(user.Id, user.FullName, user.Email);

    private AdminRow ToRow(AppUser user) =>
        new(user.Id, user.Email, user.FullName, user.IsActive, user.LockoutEnd > _time.GetUtcNow(), user.LastLoginAt, user.CreatedAt);
}
