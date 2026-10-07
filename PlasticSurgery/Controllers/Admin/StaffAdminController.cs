using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Services.PlatformAdmin;
using PlasticSurgery.Controllers.Filters;
using PlasticSurgery.Entities.Requests.PlatformAdmin;

namespace PlasticSurgery.Controllers.Admin;

/// <summary>
/// Platform-admin staff API (internal; called by the SculptFlowAdmin portal): staff accounts and clinic memberships.
/// Writes answer { clinicId }. Errors: 400 invalid input (e.g. a password the app's rules reject), 404 no such user,
/// 422 not allowed now.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/staff")]
[ApiErrors]
public class StaffAdminController : ControllerBase
{
    private readonly IStaffAdminService _staff;

    public StaffAdminController(IStaffAdminService staff)
    {
        _staff = staff;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? clinicId, [FromQuery] string? search, [FromQuery] int page, CancellationToken ct) =>
        Ok(await _staff.ListAsync(clinicId, search, page, ct));

    [HttpGet("{userId}")]
    public async Task<IActionResult> Get(string userId, CancellationToken ct) =>
        await _staff.GetAsync(userId, ct) is { } row ? Ok(row) : NotFound();

    [HttpPut("{userId}/membership-active")]
    public async Task<IActionResult> SetMembershipActive(string userId, [FromBody] ActiveBody request, CancellationToken ct) =>
        Ok(await _staff.SetMembershipActiveAsync(userId, request.Active, ct));

    [HttpPut("{userId}/locked")]
    public async Task<IActionResult> SetLocked(string userId, [FromBody] FlagBody request, CancellationToken ct) =>
        Ok(await _staff.SetLoginLockedAsync(userId, request.Value, ct));

    [HttpPut("{userId}/password")]
    public async Task<IActionResult> SetPassword(string userId, [FromBody] PasswordBody request, CancellationToken ct) =>
        Ok(await _staff.SetPasswordAsync(userId, request.Password, ct));

    [HttpPut("{userId}/email-confirmed")]
    public async Task<IActionResult> SetEmailConfirmed(string userId, [FromBody] FlagBody request, CancellationToken ct) =>
        Ok(await _staff.SetEmailConfirmedAsync(userId, request.Value, ct));
}
