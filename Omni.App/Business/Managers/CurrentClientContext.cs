using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contracts.Clients;

namespace Omni.App.Business.Managers;

public class CurrentClientContext : ICurrentClientContext
{
    private readonly IClientRepository _clients;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private UserAccess? _access;
    private bool _resolved;

    public CurrentClientContext(IClientRepository clients, IHttpContextAccessor httpContextAccessor)
    {
        _clients = clients;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<UserAccess?> GetAccessAsync(CancellationToken ct = default)
    {
        if (_resolved) return _access;
        var userId = GetUserId(_httpContextAccessor.HttpContext?.User);
        _access = userId is null ? null : await _clients.GetUserAccessAsync(userId, ct);
        _resolved = true;
        return _access;
    }

    public async Task<Client?> GetClientAsync(CancellationToken ct = default) =>
        await GetAccessAsync(ct) is { } access ? await _clients.GetReadOnlyAsync(access.ClientId, ct) : null;

    public async Task<Guid?> GetClientIdAsync(CancellationToken ct = default) =>
        (await GetAccessAsync(ct))?.ClientId;

    /// <summary>
    /// Runs on every request that carries the main app's cookie (OnValidatePrincipal): the user must still be an active
    /// client or member user with an active membership, and the cookie's security stamp must be the current one (a
    /// password change or a lock renews it). Otherwise the cookie is rejected and deleted at once, instead of the user
    /// keeping access until it expires. An admin user never passes, so an admin can't use the main app.
    /// </summary>
    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var userId = GetUserId(context.Principal);
        var clients = context.HttpContext.RequestServices.GetRequiredService<IClientRepository>();
        var access = userId is null ? null : await clients.GetUserAccessAsync(userId, context.HttpContext.RequestAborted);
        if (StillAllowed(access, context.Principal)) return;

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }

    /// <summary>The cookie still stands when the user has access and the cookie's stamp (if it carries one) matches.</summary>
    public static bool StillAllowed(UserAccess? access, ClaimsPrincipal? principal)
    {
        if (access is null) return false;
        var stamp = principal?.FindFirstValue(new IdentityOptions().ClaimsIdentity.SecurityStampClaimType);
        return stamp is null || string.Equals(stamp, access.SecurityStamp, StringComparison.Ordinal);
    }

    /// <summary>The signed-in user's id (the standard NameIdentifier claim), or null without an authenticated user.</summary>
    private static string? GetUserId(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;
}
