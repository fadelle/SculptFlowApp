using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Members;
using Omni.App.Entities.Requests.Members;
using Omni.App.Entities.Responses.Members;

namespace Omni.App.Controllers.Client;

/// <summary>The people who can sign in to the logged-in user's client, and the invites waiting to be accepted. Login
/// required; the client always comes from CurrentClientContext, never from the request. Anyone in the client can list;
/// only the <c>client</c> role may invite, resend or cancel invites and deactivate or reactivate members (403 otherwise).
/// Errors: 400 invalid input or an email that already signs in or is already invited, 404 not a member or invite of this
/// client, 422 a rule says no (seats, another active client, the client user), 502 the invite email couldn't be sent.</summary>
[ApiController]
[Route("api/members")]
public class MembersController : DashboardApiController
{
    private readonly IMemberService _members;
    private readonly IInviteService _invites;

    public MembersController(IMemberService members, IInviteService invites, ICurrentClientContext clientContext) : base(clientContext)
    {
        _members = members;
        _invites = invites;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MemberResponse>>> List(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        return Ok(await _members.ListAsync(clientId.Value, ct));
    }

    [HttpGet("invites")]
    public async Task<ActionResult<IReadOnlyList<InviteResponse>>> ListInvites(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        return Ok(await _invites.ListAsync(clientId.Value, ct));
    }

    [HttpPost("invites")]
    public async Task<ActionResult<InviteResponse>> Invite([FromBody] InviteMemberRequest request, CancellationToken ct)
    {
        var access = await GetAccessAsync(ct);
        if (access is null) return Forbid();
        var invite = await _invites.InviteAsync(access, request, RequestBaseUrl, ct);
        return StatusCode(StatusCodes.Status201Created, invite);
    }

    [HttpPost("invites/{inviteId:guid}/resend")]
    public async Task<ActionResult<InviteResponse>> ResendInvite(Guid inviteId, CancellationToken ct)
    {
        var access = await GetAccessAsync(ct);
        if (access is null) return Forbid();
        return Ok(await _invites.ResendAsync(access, inviteId, RequestBaseUrl, ct));
    }

    [HttpDelete("invites/{inviteId:guid}")]
    public async Task<IActionResult> RevokeInvite(Guid inviteId, CancellationToken ct)
    {
        var access = await GetAccessAsync(ct);
        if (access is null) return Forbid();
        await _invites.RevokeAsync(access, inviteId, ct);
        return NoContent();
    }

    [HttpPost("{userId}/deactivate")]
    public Task<IActionResult> Deactivate(string userId, CancellationToken ct) => SetActiveAsync(userId, false, ct);

    [HttpPost("{userId}/reactivate")]
    public Task<IActionResult> Reactivate(string userId, CancellationToken ct) => SetActiveAsync(userId, true, ct);

    private async Task<IActionResult> SetActiveAsync(string userId, bool active, CancellationToken ct)
    {
        var access = await GetAccessAsync(ct);
        if (access is null) return Forbid();
        await _members.SetActiveAsync(access, userId, active, ct);
        return NoContent();
    }

    private string RequestBaseUrl => $"{Request.Scheme}://{Request.Host}";
}
