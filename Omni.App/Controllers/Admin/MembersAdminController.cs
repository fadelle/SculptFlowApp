using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Requests.PlatformAdmin;

namespace Omni.App.Controllers.Admin;

/// <summary>
/// Platform-admin members API (internal; called by the Omni.Admin portal): members accounts and client memberships.
/// Writes answer { clientId }. Errors: 400 invalid input (e.g. a password the app's rules reject), 404 no such user,
/// 422 not allowed now.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/members")]
[ApiErrors]
public class MembersAdminController : ControllerBase
{
    private readonly IMemberAdminService _members;

    public MembersAdminController(IMemberAdminService members)
    {
        _members = members;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? clientId, [FromQuery] string? search, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _members.ListAsync(clientId, search, page, ct));

    [HttpGet("{userId}")]
    public async Task<IActionResult> Get(string userId, CancellationToken ct) =>
        await _members.GetAsync(userId, ct) is { } row ? Ok(row) : NotFound();

    [HttpPut("{userId}/membership-active")]
    public async Task<IActionResult> SetMembershipActive(string userId, [FromBody] ActiveBody request, CancellationToken ct) =>
        Ok(await _members.SetMembershipActiveAsync(userId, request.Active, ct));

    [HttpPut("{userId}/locked")]
    public async Task<IActionResult> SetLocked(string userId, [FromBody] FlagBody request, CancellationToken ct) =>
        Ok(await _members.SetLoginLockedAsync(userId, request.Value, ct));

    [HttpPut("{userId}/password")]
    public async Task<IActionResult> SetPassword(string userId, [FromBody] PasswordBody request, CancellationToken ct) =>
        Ok(await _members.SetPasswordAsync(userId, request.Password, ct));

    /// <summary>Emails the user a password reset link. 404 unknown or admin user, 502 the email couldn't be sent.</summary>
    [HttpPost("{userId}/password-reset-email")]
    public async Task<IActionResult> SendPasswordResetEmail(string userId, CancellationToken ct) =>
        Ok(await _members.SendPasswordResetEmailAsync(userId, $"{Request.Scheme}://{Request.Host}", ct));

    [HttpPut("{userId}/email-confirmed")]
    public async Task<IActionResult> SetEmailConfirmed(string userId, [FromBody] FlagBody request, CancellationToken ct) =>
        Ok(await _members.SetEmailConfirmedAsync(userId, request.Value, ct));
}
