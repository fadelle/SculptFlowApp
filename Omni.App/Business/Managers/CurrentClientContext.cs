using Microsoft.AspNetCore.Http;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contracts.Clients;

namespace Omni.App.Business.Managers;

public class CurrentClientContext : ICurrentClientContext
{
    private readonly IClientRepository _clients;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentClientContext(IClientRepository clients, IHttpContextAccessor httpContextAccessor)
    {
        _clients = clients;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<Client?> GetClientAsync(CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId is null) return null;

        return await _clients.GetClientOfActiveMemberAsync(userId, ct);
    }

    public async Task<Guid?> GetClientIdAsync(CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId is null) return null;

        return await _clients.GetClientIdOfActiveMemberAsync(userId, ct);
    }

    /// <summary>The logged-in IdentityUser's Id (the standard ASP.NET Core Identity claim), or
    /// null if there's no authenticated user on this request at all.</summary>
    private string? GetUserId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return null;
        return user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    }
}
