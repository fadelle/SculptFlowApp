using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Members;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Requests.Members;
using Omni.App.Entities.Responses.Members;

namespace Omni.App.Pages.Members;

/// <summary>Members: the people who can sign in to the current client. Everyone sees the list; the client user (role
/// <c>client</c>) also adds members with a password and deactivates/reactivates them (IMemberService enforces that and
/// the seat limit). The client always comes from the signed-in user (ICurrentClientContext).</summary>
public class IndexModel : PageModel
{
    private const string DeactivatedKey = "members.deactivated";
    private const string ReactivatedKey = "members.reactivated";

    private readonly ICurrentClientContext _clientContext;
    private readonly IMemberService _members;
    private readonly IEntitlementService _entitlements;
    private readonly IStringLocalizer _text;

    public IndexModel(ICurrentClientContext clientContext, IMemberService members, IEntitlementService entitlements, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _members = members;
        _entitlements = entitlements;
        _text = text;
    }

    public bool ClientConfigured { get; private set; }
    public string ClientName { get; private set; } = string.Empty;
    public string? CurrentUserId { get; private set; }
    /// <summary>The signed-in user is the client user, so the add form and the row actions are shown.</summary>
    public bool CanManage { get; private set; }
    public IReadOnlyList<MemberResponse> Members { get; private set; } = Array.Empty<MemberResponse>();
    public int SeatsInUse { get; private set; }
    /// <summary>The plan's seats (max_users); null = unlimited.</summary>
    public int? SeatLimit { get; private set; }

    [BindProperty] public string? FullName { get; set; }
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string? Password { get; set; }

    public string? ErrorMessage { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAddAsync(CancellationToken ct)
    {
        var access = await _clientContext.GetAccessAsync(ct);
        if (access is null) return Forbid();
        try
        {
            var added = await _members.AddAsync(access, new AddMemberRequest(FullName, Email, Password), ct);
            StatusMessage = _text["members.add.done", added.FullName ?? added.Email ?? string.Empty].Value;
            return RedirectToPage();
        }
        catch (Exception ex) when (IsRuleError(ex))
        {
            ErrorMessage = _text.Localize(ex);
            Password = null;
            await LoadAsync(ct);
            return Page();
        }
    }

    public Task<IActionResult> OnPostDeactivateAsync(string userId, string? name, CancellationToken ct) =>
        SetActiveAsync(userId, false, DeactivatedKey, name, ct);

    public Task<IActionResult> OnPostReactivateAsync(string userId, string? name, CancellationToken ct) =>
        SetActiveAsync(userId, true, ReactivatedKey, name, ct);

    private async Task<IActionResult> SetActiveAsync(string userId, bool active, string doneKey, string? name, CancellationToken ct)
    {
        var access = await _clientContext.GetAccessAsync(ct);
        if (access is null) return Forbid();
        try
        {
            await _members.SetActiveAsync(access, userId, active, ct);
            StatusMessage = _text[doneKey, name ?? string.Empty].Value;
            return RedirectToPage();
        }
        catch (Exception ex) when (IsRuleError(ex))
        {
            ErrorMessage = _text.Localize(ex);
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
        SeatsInUse = Members.Count(m => m.IsActive);
        SeatLimit = (await _entitlements.GetAsync(client.Id, ct)).MaximumUsers;
    }

    /// <summary>What the service throws for a refused change (shown on the page); anything else is a real error.</summary>
    private static bool IsRuleError(Exception ex) =>
        ex is ArgumentException or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException;
}
