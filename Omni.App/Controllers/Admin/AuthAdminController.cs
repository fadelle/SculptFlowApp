using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Services.PlatformAdmin;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Requests.PlatformAdmin;

namespace Omni.App.Controllers.Admin;

/// <summary>
/// Admin sign-in for the Omni.Admin portal (internal; server to server behind the platform-admin key). The portal has no
/// users table of its own: it posts what the admin typed to <c>login</c>, issues its own cookie from the answer, and
/// asks <c>admins/{id}</c> on every request whether that admin is still an active admin. Only role <c>admin</c> passes;
/// any failure is one generic 401 so nobody learns which emails exist.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/auth")]
[ApiErrors]
public class AuthAdminController : ControllerBase
{
    /// <summary>The one answer for every failed sign-in (also a locked account).</summary>
    public const string SignInFailed = "Wrong email or password, or the account is locked for a few minutes.";

    private readonly IAdminAccountService _admins;

    public AuthAdminController(IAdminAccountService admins)
    {
        _admins = admins;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] AdminLoginRequest request, CancellationToken ct) =>
        await _admins.SignInAsync(request.Email, request.Password, ct) is { } admin
            ? Ok(admin)
            : Unauthorized(new { error = SignInFailed });

    [HttpGet("admins/{id}")]
    public async Task<IActionResult> GetActiveAdmin(string id, CancellationToken ct) =>
        await _admins.GetActiveAsync(id, ct) is { } admin ? Ok(admin) : NotFound();
}
