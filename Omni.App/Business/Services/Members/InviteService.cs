using Microsoft.AspNetCore.Identity;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Email;
using Omni.App.Business.Contracts.Services.Members;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Entities.Dtos.Notifications;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Dtos.Members;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Members;
using Omni.App.Entities.Responses.Members;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Users;

namespace Omni.App.Business.Services.Members;

public class InviteService : IInviteService
{
    private const int MaxNameLength = 200;
    private const string LinkInvalidKey = "acceptInvite.errors.linkInvalid";

    private readonly IAccountTokenRepository _tokens;
    private readonly IClientRepository _clients;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UserManager<AppUser> _users;
    private readonly IEntitlementService _entitlements;
    private readonly IAccountEmailService _emails;
    private readonly IConfigManager _config;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;
    private readonly ILogger<InviteService> _logger;

    public InviteService(IAccountTokenRepository tokens, IClientRepository clients, IUnitOfWork unitOfWork, UserManager<AppUser> users,
        IEntitlementService entitlements, IAccountEmailService emails, IConfigManager config, IConfiguration configuration, TimeProvider time,
        ILogger<InviteService> logger, Contracts.Services.Notifications.INotificationService? notifications = null)
    {
        _notifications = notifications;
        _tokens = tokens;
        _clients = clients;
        _unitOfWork = unitOfWork;
        _users = users;
        _entitlements = entitlements;
        _emails = emails;
        _config = config;
        _configuration = configuration;
        _time = time;
        _logger = logger;
    }

    public async Task<IReadOnlyList<InviteResponse>> ListAsync(Guid clientId, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        return (await _tokens.ListPendingInvitesAsync(clientId, ct)).Select(t => ToResponse(t, now)).ToList();
    }

    public async Task<InviteResponse> InviteAsync(UserAccess actor, InviteMemberRequest request, string requestBaseUrl, CancellationToken ct = default)
    {
        MemberRules.EnsureClientRole(actor);
        var email = (request.Email ?? string.Empty).Trim();
        var fullName = string.IsNullOrWhiteSpace(request.FullName) ? null : request.FullName.Trim();
        if (!EmailAddress.IsValid(email)) throw new ArgumentException("members.errors.emailInvalid");
        if (fullName?.Length > MaxNameLength) throw new ArgumentException("members.errors.nameTooLong").WithArgs(MaxNameLength);
        // Sign-in names are emails, so one address is one user in every app (admins included).
        if (await _users.FindByNameAsync(email) is not null || await _users.FindByEmailAsync(email) is not null)
            throw new ArgumentException("members.errors.emailTaken");
        var now = _time.GetUtcNow();
        if (await _tokens.HasOpenInviteAsync(actor.ClientId, email, now, ct)) throw new ArgumentException("members.errors.alreadyInvited");
        await EnsureSeatAsync(actor.ClientId, now, exceptInviteId: null, ct);

        var invite = new AccountToken
        {
            Id = Guid.NewGuid(), Purpose = AccountTokenPurpose.Invite, ClientId = actor.ClientId, Email = email, FullName = fullName,
            CreatedByUserId = actor.UserId, CreatedAt = now
        };
        await IssueAndSendAsync(invite, actor.ClientId, requestBaseUrl, now,
            (client, link, days) => _emails.SendInviteAsync(invite.Email, invite.FullName, actor.FullName, client, actor.Language, link, days, ct), ct);
        return ToResponse(invite, now);
    }

    public async Task<InviteResponse> InviteOwnerAsync(Guid clientId, string? email, string? fullName, string requestBaseUrl, CancellationToken ct = default)
    {
        var address = (email ?? string.Empty).Trim();
        var name = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
        if (!EmailAddress.IsValid(address)) throw new ArgumentException("members.errors.emailInvalid");
        if (name?.Length > MaxNameLength) throw new ArgumentException("members.errors.nameTooLong").WithArgs(MaxNameLength);
        _ = await _clients.GetReadOnlyAsync(clientId, ct) ?? throw new KeyNotFoundException("clients.errors.notFound");
        if (await _clients.HasActiveOwnerAsync(clientId, ct)) throw new ArgumentException("clients.errors.ownerExists");
        if (await _users.FindByNameAsync(address) is not null || await _users.FindByEmailAsync(address) is not null)
            throw new ArgumentException("members.errors.emailTaken");

        var now = _time.GetUtcNow();
        // One owner link at a time: an earlier one (another address, or a resend) stops working.
        foreach (var old in await _tokens.ListPendingOwnerInvitesAsync(clientId, ct)) old.RevokedAt = now;
        var invite = new AccountToken
        {
            Id = Guid.NewGuid(), Purpose = AccountTokenPurpose.OwnerInvite, ClientId = clientId, Email = address, FullName = name, CreatedAt = now
        };
        await IssueAndSendAsync(invite, clientId, requestBaseUrl, now,
            (client, link, days) => _emails.SendOwnerInviteAsync(invite.Email, invite.FullName, client, link, days, ct), ct);
        return ToResponse(invite, now);
    }

    public async Task<InviteResponse> ResendOwnerInviteAsync(Guid clientId, string requestBaseUrl, CancellationToken ct = default)
    {
        // After the owner joined, their invite is used: say so rather than "never invited".
        if (await _clients.HasActiveOwnerAsync(clientId, ct)) throw new ArgumentException("clients.errors.ownerExists");
        var latest = (await _tokens.ListPendingOwnerInvitesAsync(clientId, ct)).FirstOrDefault()
            ?? throw new KeyNotFoundException("clients.errors.noOwnerInvite");
        return await InviteOwnerAsync(clientId, latest.Email, latest.FullName, requestBaseUrl, ct);
    }

    public async Task<InviteResponse> ResendAsync(UserAccess actor, Guid inviteId, string requestBaseUrl, CancellationToken ct = default)
    {
        MemberRules.EnsureClientRole(actor);
        var old = await _tokens.GetInviteAsync(actor.ClientId, inviteId, ct);
        if (old is null || old.UsedAt is not null || old.RevokedAt is not null) throw new KeyNotFoundException("members.errors.inviteNotFound");
        var now = _time.GetUtcNow();
        // An expired invite no longer holds a seat; giving it a new link takes one again.
        if (now >= old.ExpiresAt) await EnsureSeatAsync(actor.ClientId, now, exceptInviteId: old.Id, ct);

        old.RevokedAt = now;
        var invite = new AccountToken
        {
            Id = Guid.NewGuid(), Purpose = AccountTokenPurpose.Invite, ClientId = actor.ClientId, Email = old.Email, FullName = old.FullName,
            CreatedByUserId = actor.UserId, CreatedAt = now
        };
        await IssueAndSendAsync(invite, actor.ClientId, requestBaseUrl, now,
            (client, link, days) => _emails.SendInviteAsync(invite.Email, invite.FullName, actor.FullName, client, actor.Language, link, days, ct), ct);
        return ToResponse(invite, now);
    }

    public async Task RevokeAsync(UserAccess actor, Guid inviteId, CancellationToken ct = default)
    {
        MemberRules.EnsureClientRole(actor);
        var invite = await _tokens.GetInviteAsync(actor.ClientId, inviteId, ct);
        await RevokeAsync(invite, ct);
    }

    public async Task<Guid> RevokeAnyAsync(Guid inviteId, CancellationToken ct = default)
    {
        var invite = await _tokens.GetInviteAsync(inviteId, ct);
        await RevokeAsync(invite, ct);
        return invite!.ClientId!.Value;
    }

    public async Task<InviteLanding?> GetLandingAsync(string? token, CancellationToken ct = default)
    {
        var found = await FindUsableAsync(token, ct);
        return found is { } f ? new InviteLanding(f.Client.Name, f.Invite.Email, f.Invite.FullName) : null;
    }

    /// <summary>The invitee's words when the plan has no free seat for them (QA t1-accounts-11).</summary>
    public const string NoSeatKey = "acceptInvite.errors.noSeat";

    private readonly Contracts.Services.Notifications.INotificationService? _notifications;

    private async Task NotifyNoSeatAsync(Guid clientId, string email, CancellationToken ct)
    {
        if (_notifications is null) return;
        try
        {
            await _notifications.CreateAsync(clientId, NotificationType.Billing,
                NotificationText.Of("notifications.billing.inviteNoSeat.title", "notifications.billing.inviteNoSeat.message", email),
                link: "/Members", ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "The no-seat notice for client {ClientId} wasn't created.", clientId);
        }
    }

    public async Task<AppUser> AcceptAsync(string? token, string? fullName, string? password, string? confirmPassword, CancellationToken ct = default)
    {
        var name = (fullName ?? string.Empty).Trim();
        if (name.Length == 0) throw new ArgumentException("acceptInvite.errors.nameRequired");
        if (name.Length > MaxNameLength) throw new ArgumentException("members.errors.nameTooLong").WithArgs(MaxNameLength);
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(confirmPassword)) throw new ArgumentException("resetPassword.errors.missingFields");
        if (password != confirmPassword) throw new ArgumentException("resetPassword.errors.mismatch");
        var (invite, client) = await FindUsableAsync(token, ct) ?? throw new InvalidOperationException(LinkInvalidKey);
        if (await _users.FindByNameAsync(invite.Email) is not null || await _users.FindByEmailAsync(invite.Email) is not null)
            throw new ArgumentException("members.errors.emailTaken");
        var now = _time.GetUtcNow();
        var owner = invite.Purpose == AccountTokenPurpose.OwnerInvite;
        if (owner)
        {
            // The client's first user: no seat question, but never a second owner.
            if (await _clients.HasActiveOwnerAsync(client.Id, ct)) throw new InvalidOperationException(LinkInvalidKey);
        }
        else
        {
            // The invite held this seat; the plan may have shrunk since, so check again without counting the invite twice.
            try
            {
                await EnsureSeatAsync(client.Id, now, exceptInviteId: invite.Id, ct);
            }
            catch (EntitlementDeniedException)
            {
                // The invitee has no account and no Billing page (QA t1-accounts-11): tell them in their own words, and tell the
                // team, who can free or add a seat.
                await NotifyNoSeatAsync(client.Id, invite.Email, ct);
                throw new InvalidOperationException(NoSeatKey);
            }
        }

        var user = new AppUser
        {
            UserName = invite.Email, Email = invite.Email, EmailConfirmed = true, FullName = name, Role = owner ? UserRole.Client : UserRole.Member,
            IsActive = true, CreatedAt = now, UpdatedAt = now
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
            Id = Guid.NewGuid(), ClientId = client.Id, UserId = user.Id, IsActive = true, CreatedAt = now, UpdatedAt = now
        });
        invite.UsedAt = now;
        invite.UserId = user.Id;
        await _unitOfWork.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _logger.LogInformation("Invite {InviteId} accepted: user {UserId} joined client {ClientId}.", invite.Id, user.Id, client.Id);
        return user;
    }

    private async Task RevokeAsync(AccountToken? invite, CancellationToken ct)
    {
        if (invite is null || invite.UsedAt is not null) throw new KeyNotFoundException("members.errors.inviteNotFound");
        if (invite.RevokedAt is not null) return;
        invite.RevokedAt = _time.GetUtcNow();
        await _unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>Saves the invite with a fresh token, then emails the link. A failed email removes the new invite again
    /// (and restores a resent one), so nobody is left with an invite they never got.</summary>
    private async Task IssueAndSendAsync(AccountToken invite, Guid clientId, string requestBaseUrl, DateTimeOffset now,
        Func<Client, string, int, Task> send, CancellationToken ct)
    {
        var days = _config.AccountsInviteDays;
        var raw = AccountTokenCodec.NewToken();
        invite.TokenHash = AccountTokenCodec.Hash(raw)!;
        invite.ExpiresAt = now.AddDays(days);
        var client = await _clients.GetReadOnlyAsync(clientId, ct) ?? throw new KeyNotFoundException("members.errors.notFound");
        var link = AccountLinks.Build(AccountLinks.Origin(_configuration["App:PublicBaseUrl"], requestBaseUrl), AccountLinks.AcceptInvitePath, raw);

        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        _tokens.Add(invite);
        await _unitOfWork.SaveChangesAsync(ct);
        try
        {
            await send(client, link, days);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            _unitOfWork.DiscardChanges();
            throw;
        }
        await tx.CommitAsync(ct);
        _logger.LogInformation("Invite {InviteId} ({Purpose}) sent for client {ClientId}.", invite.Id, invite.Purpose, clientId);
    }

    private async Task EnsureSeatAsync(Guid clientId, DateTimeOffset now, Guid? exceptInviteId, CancellationToken ct)
    {
        var held = await _clients.CountSeatsInUseAsync(clientId, ct) + await _tokens.CountOpenInvitesAsync(clientId, now, exceptInviteId, ct);
        await _entitlements.EnsureWithinLimitAsync(clientId, EntitlementKeys.MaxUsers, held + 1, ct);
    }

    private async Task<(AccountToken Invite, Client Client)?> FindUsableAsync(string? token, CancellationToken ct)
    {
        var hash = AccountTokenCodec.Hash(token);
        if (hash is null) return null;
        var invite = await _tokens.GetByHashAsync(hash, ct);
        if (invite is null || invite.Purpose is not (AccountTokenPurpose.Invite or AccountTokenPurpose.OwnerInvite) || invite.ClientId is null
            || !invite.IsOpen(_time.GetUtcNow())) return null;
        var client = await _clients.GetReadOnlyAsync(invite.ClientId.Value, ct);
        return client is { IsActive: true } ? (invite, client) : null;
    }

    private static InviteResponse ToResponse(AccountToken t, DateTimeOffset now) =>
        new(t.Id, t.Email, t.FullName, t.CreatedAt, t.ExpiresAt, now >= t.ExpiresAt);
}
