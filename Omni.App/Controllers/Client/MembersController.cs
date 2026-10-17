using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Members;
using Omni.App.Entities.Requests.Members;
using Omni.App.Entities.Responses.Members;

namespace Omni.App.Controllers.Client;

/// <summary>The people who can sign in to the logged-in user's client. Login required; the client always comes from
/// CurrentClientContext, never from the request. Anyone in the client can list; only the <c>client</c> role may add,
/// deactivate or reactivate members (403 otherwise). Errors: 400 invalid input, 404 not a member of this client,
/// 422 a rule says no (seats, another active client, the client user).</summary>
[ApiController]
[Route("api/members")]
public class MembersController : DashboardApiController
{
    private readonly IMemberService _members;

    public MembersController(IMemberService members, ICurrentClientContext clientContext) : base(clientContext)
    {
        _members = members;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MemberResponse>>> List(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        return Ok(await _members.ListAsync(clientId.Value, ct));
    }

    [HttpPost]
    public async Task<ActionResult<MemberResponse>> Add([FromBody] AddMemberRequest request, CancellationToken ct)
    {
        var access = await GetAccessAsync(ct);
        if (access is null) return Forbid();
        var member = await _members.AddAsync(access, request, ct);
        return StatusCode(StatusCodes.Status201Created, member);
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
}
