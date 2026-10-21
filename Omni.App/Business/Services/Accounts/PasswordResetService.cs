using Microsoft.AspNetCore.Identity;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Accounts;
using Omni.App.Business.Contracts.Services.Email;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Users;

namespace Omni.App.Business.Services.Accounts;

public class PasswordResetService : IPasswordResetService
{
    private const string MissingFieldsKey = "resetPassword.errors.missingFields";
    private const string MismatchKey = "resetPassword.errors.mismatch";
    private const string LinkInvalidKey = "resetPassword.errors.linkInvalid";

    private readonly UserManager<AppUser> _users;
    private readonly IAccountTokenRepository _tokens;
    private readonly IClientRepository _clients;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAccountEmailService _emails;
    private readonly IConfigManager _config;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;
    private readonly ILogger<PasswordResetService> _logger;

    public PasswordResetService(UserManager<AppUser> users, IAccountTokenRepository tokens, IClientRepository clients, IUnitOfWork unitOfWork,
        IAccountEmailService emails, IConfigManager config, IConfiguration configuration, TimeProvider time, ILogger<PasswordResetService> logger)
    {
        _users = users;
        _tokens = tokens;
        _clients = clients;
        _unitOfWork = unitOfWork;
        _emails = emails;
        _config = config;
        _configuration = configuration;
        _time = time;
        _logger = logger;
    }

    public async Task RequestAsync(string? email, string requestBaseUrl, string? pageLanguage, CancellationToken ct = default)
    {
        var address = (email ?? string.Empty).Trim();
        if (!EmailAddress.IsValid(address)) return;
        var user = await _users.FindByEmailAsync(address) ?? await _users.FindByNameAsync(address);
        if (user is null || !user.IsActive || !UserRole.UsesMainApp(user.Role))
        {
            _logger.LogInformation("Password reset asked for an address with no main-app account; nothing sent.");
            return;
        }

        var now = _time.GetUtcNow();
        var sent = await _tokens.CountForUserSinceAsync(AccountTokenPurpose.PasswordReset, user.Id, now.AddHours(-1), ct);
        if (sent >= _config.AccountsResetEmailsPerHour)
        {
            _logger.LogInformation("Password reset for user {UserId} skipped: {Sent} links in the last hour.", user.Id, sent);
            return;
        }

        try
        {
            await IssueAndSendAsync(user, requestBaseUrl, pageLanguage, ct);
        }
        catch (EmailSendException ex)
        {
            _logger.LogError(ex, "Password reset email for user {UserId} could not be sent.", user.Id);
        }
    }

    public async Task SendForUserAsync(string userId, string requestBaseUrl, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user is null || !UserRole.UsesMainApp(user.Role)) throw new KeyNotFoundException("User not found."); // text-ok: platform-admin API (English)
        await IssueAndSendAsync(user, requestBaseUrl, pageLanguage: null, ct);
    }

    public async Task<bool> IsUsableAsync(string? token, CancellationToken ct = default) =>
        await FindUsableAsync(token, ct) is not null;

    public async Task ResetAsync(string? token, string? password, string? confirmPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(confirmPassword)) throw new ArgumentException(MissingFieldsKey);
        if (password != confirmPassword) throw new ArgumentException(MismatchKey);
        var found = await FindUsableAsync(token, ct) ?? throw new InvalidOperationException(LinkInvalidKey);
        var (link, user) = found;

        // Check the rules first, so a refused password never leaves the account without one.
        var problems = new List<string>();
        foreach (var validator in _users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(_users, user, password);
            if (!result.Succeeded) problems.AddRange(result.Errors.Select(e => e.Description));
        }
        if (problems.Count > 0) throw new ArgumentException(string.Join(" ", problems));

        var now = _time.GetUtcNow();
        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        if (await _users.HasPasswordAsync(user)) Check(await _users.RemovePasswordAsync(user));
        // The link proved the person reads this mailbox.
        user.EmailConfirmed = true;
        user.UpdatedAt = now;
        Check(await _users.AddPasswordAsync(user, password)); // renews the security stamp: other sessions sign out
        Check(await _users.ResetAccessFailedCountAsync(user));
        Check(await _users.SetLockoutEndDateAsync(user, null));

        link.UsedAt = now;
        foreach (var other in await _tokens.ListOpenForUserAsync(AccountTokenPurpose.PasswordReset, user.Id, now, ct))
        {
            if (other.Id != link.Id) other.RevokedAt = now;
        }
        await _unitOfWork.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _logger.LogInformation("User {UserId} chose a new password with a reset link.", user.Id);
    }

    private async Task IssueAndSendAsync(AppUser user, string requestBaseUrl, string? pageLanguage, CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var minutes = _config.AccountsResetLinkMinutes;
        var raw = AccountTokenCodec.NewToken();
        _tokens.Add(new AccountToken
        {
            Id = Guid.NewGuid(), Purpose = AccountTokenPurpose.PasswordReset, UserId = user.Id, Email = user.Email ?? user.UserName ?? string.Empty,
            TokenHash = AccountTokenCodec.Hash(raw)!, ExpiresAt = now.AddMinutes(minutes), CreatedAt = now
        });
        await _unitOfWork.SaveChangesAsync(ct);

        var access = await _clients.GetUserAccessAsync(user.Id, ct);
        var client = access is null ? null : await _clients.GetReadOnlyAsync(access.ClientId, ct);
        var link = AccountLinks.Build(AccountLinks.Origin(_configuration["App:PublicBaseUrl"], requestBaseUrl), AccountLinks.ResetPasswordPath, raw);
        await _emails.SendPasswordResetAsync(user, client, pageLanguage, link, minutes, ct);
        _logger.LogInformation("Password reset link sent to user {UserId}.", user.Id);
    }

    /// <summary>The open reset link with this token and its user, when that user may still sign in to the main app.</summary>
    private async Task<(AccountToken Link, AppUser User)?> FindUsableAsync(string? token, CancellationToken ct)
    {
        var hash = AccountTokenCodec.Hash(token);
        if (hash is null) return null;
        var link = await _tokens.GetByHashAsync(hash, ct);
        if (link is null || link.Purpose != AccountTokenPurpose.PasswordReset || link.UserId is null || !link.IsOpen(_time.GetUtcNow())) return null;
        var user = await _users.FindByIdAsync(link.UserId);
        return user is { IsActive: true } && UserRole.UsesMainApp(user.Role) ? (link, user) : null;
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new ArgumentException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
