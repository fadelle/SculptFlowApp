using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Members;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Requests.Members;
using Omni.App.Entities.Responses.Members;

namespace Omni.App.Pages.Members;

/// <summary>Members: the people who can sign in to the current client, and the invites waiting to be accepted. Everyone
/// sees both lists; the client user (role <c>client</c>) also invites people by email, resends or cancels invites and
/// deactivates/reactivates members (IMemberService and IInviteService enforce that and the seat limit). The client always
/// comes from the signed-in user (ICurrentClientContext).</summary>
public class IndexModel : PageModel
{
    private const string DeactivatedKey = "members.deactivated";
    private const string ReactivatedKey = "members.reactivated";
    private const string InvitedKey = "members.invite.done";
    private const string ResentKey = "members.invites.resent";
    private const string RevokedKey = "members.invites.revoked";

    private readonly ICurrentClientContext _clientContext;
    private readonly IMemberService _members;
    private readonly IInviteService _invites;
    private readonly IEntitlementService _entitlements;
    private readonly IStringLocalizer _text;

    public IndexModel(ICurrentClientContext clientContext, IMemberService members, IInviteService invites, IEntitlementService entitlements,
        IStringLocalizer text)
    {
        _clientContext = clientContext;
        _members = members;
        _invites = invites;
        _entitlements = entitlements;
        _text = text;
    }

    public bool ClientConfigured { get; private set; }
    public string ClientName { get; private set; } = string.Empty;
    public string? CurrentUserId { get; private set; }
    /// <summary>The signed-in user is the client user, so the invite form and the row actions are shown.</summary>
    public bool CanManage { get; private set; }
    public IReadOnlyList<MemberResponse> Members { get; private set; } = Array.Empty<MemberResponse>();
    public IReadOnlyList<InviteResponse> Invites { get; private set; } = Array.Empty<InviteResponse>();
    /// <summary>Active people plus invites whose link still works (an open invite holds a seat).</summary>
    public int SeatsInUse { get; private set; }
    /// <summary>The plan's seats (max_users); null = unlimited.</summary>
    public int? SeatLimit { get; private set; }

    [BindProperty] public string? FullName { get; set; }
    [BindProperty] public string? Email { get; set; }

    public string? ErrorMessage { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostInviteAsync(CancellationToken ct)
    {
        var access = await _clientContext.GetAccessAsync(ct);
        if (access is null) return Forbid();
        try
        {
            var invite = await _invites.InviteAsync(access, new InviteMemberRequest(Email, FullName), RequestBaseUrl, ct);
            StatusMessage = _text[InvitedKey, invite.Email].Value;
            return RedirectToPage();
        }
        catch (Exception ex) when (IsRuleError(ex))
        {
            ErrorMessage = Message(ex);
            await LoadAsync(ct);
            return Page();
        }
    }

    public Task<IActionResult> OnPostResendInviteAsync(Guid inviteId, string? email, CancellationToken ct) =>
        RunAsync(async access => await _invites.ResendAsync(access, inviteId, RequestBaseUrl, ct), ResentKey, email, ct);

    public Task<IActionResult> OnPostRevokeInviteAsync(Guid inviteId, string? email, CancellationToken ct) =>
        RunAsync(access => _invites.RevokeAsync(access, inviteId, ct), RevokedKey, email, ct);

    public Task<IActionResult> OnPostDeactivateAsync(string userId, string? name, CancellationToken ct) =>
        RunAsync(access => _members.SetActiveAsync(access, userId, false, ct), DeactivatedKey, name, ct);

    public Task<IActionResult> OnPostReactivateAsync(string userId, string? name, CancellationToken ct) =>
        RunAsync(access => _members.SetActiveAsync(access, userId, true, ct), ReactivatedKey, name, ct);

    private async Task<IActionResult> RunAsync(Func<UserAccess, Task> change, string doneKey, string? name, CancellationToken ct)
    {
        var access = await _clientContext.GetAccessAsync(ct);
        if (access is null) return Forbid();
        try
        {
            await change(access);
            StatusMessage = _text[doneKey, name ?? string.Empty].Value;
            return RedirectToPage();
        }
        catch (Exception ex) when (IsRuleError(ex))
        {
            ErrorMessage = Message(ex);
            await LoadAsync(ct);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var access = await _clientContext.GetAccessAsync(ct);
        var client = await _clientContext.GetClientAsync(ct);
        if (access is null || client is null) return;

        ClientConfigured = true;
        ClientName = client.Name;
        CurrentUserId = access.UserId;
        CanManage = access.Role == UserRole.Client;
        Members = await _members.ListAsync(client.Id, ct);
        Invites = await _invites.ListAsync(client.Id, ct);
        SeatsInUse = Members.Count(m => m.IsActive) + Invites.Count(i => !i.IsExpired);
        SeatLimit = (await _entitlements.GetAsync(client.Id, ct)).MaximumUsers;
    }

    private string RequestBaseUrl => $"{Request.Scheme}://{Request.Host}";

    /// <summary>An email failure shows the fixed "couldn't send" text, never the provider's reason.</summary>
    private string Message(Exception ex) => ex is EmailSendException ? _text[EmailSendException.TextKey].Value : _text.Localize(ex);

    /// <summary>What the services throw for a refused change (shown on the page); anything else is a real error.</summary>
    private static bool IsRuleError(Exception ex) =>
        ex is ArgumentException or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException or EmailSendException;
}
