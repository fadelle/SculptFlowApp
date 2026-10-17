using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Requests.PlatformAdmin;

namespace Omni.App.Controllers.Admin;

/// <summary>
/// Platform-admin admins API (internal; called by the Omni.Admin portal's Admins page): admin users in identity.users.
/// Writes answer the admin's row (the portal writes its own audit entry). Errors: 400 invalid input or a refused change
/// (your own account, the last active admin, a weak password), 404 no such admin.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/admins")]
[ApiErrors]
public class AdminsAdminController : ControllerBase
{
    private readonly IAdminAccountService _admins;

    public AdminsAdminController(IAdminAccountService admins)
    {
        _admins = admins;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await _admins.ListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAdminRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await _admins.CreateAsync(request.Email, request.FullName, request.Password, ct));

    [HttpPut("{id}/active")]
    public async Task<IActionResult> SetActive(string id, [FromBody] AdminActiveBody request, CancellationToken ct) =>
        Ok(await _admins.SetActiveAsync(id, request.Active, request.ActingAdminId, ct));

    [HttpPut("{id}/password")]
    public async Task<IActionResult> SetPassword(string id, [FromBody] PasswordBody request, CancellationToken ct) =>
        Ok(await _admins.SetPasswordAsync(id, request.Password, ct));
}
