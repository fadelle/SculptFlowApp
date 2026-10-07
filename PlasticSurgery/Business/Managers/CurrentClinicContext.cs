using Microsoft.AspNetCore.Http;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contracts.Clinics;

namespace PlasticSurgery.Business.Managers;

public class CurrentClinicContext : ICurrentClinicContext
{
    private readonly IClinicRepository _clinics;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentClinicContext(IClinicRepository clinics, IHttpContextAccessor httpContextAccessor)
    {
        _clinics = clinics;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<Clinic?> GetClinicAsync(CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId is null) return null;

        return await _clinics.GetClinicOfActiveMemberAsync(userId, ct);
    }

    public async Task<Guid?> GetClinicIdAsync(CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId is null) return null;

        return await _clinics.GetClinicIdOfActiveMemberAsync(userId, ct);
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
