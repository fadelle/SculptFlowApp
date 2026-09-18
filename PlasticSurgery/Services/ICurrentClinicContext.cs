using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Services;

/// <summary>
/// Resolves "the current clinic" for every dashboard Razor Page and dashboard-facing API — from
/// the authenticated Identity user via clinic_users, never from a clinicId the browser supplies.
/// MVP assumes one active membership per user (see ClinicUser's doc comment): the first matching
/// row wins if there's ever more than one.
///
/// This is the direct replacement for the old IClinicContext.GetDefaultClinicAsync() pattern
/// everywhere except: (1) the WhatsApp webhook path, which resolves clinic from the Meta
/// WABA/phone_number_id instead (see Integrations/WhatsApp/MetaWebhookProcessor.cs — unaffected by
/// this class), and (2) new-user registration, which still uses IClinicContext once to bootstrap
/// the first clinic_users row (see Pages/Account/Register.cshtml.cs).
/// </summary>
public interface ICurrentClinicContext
{
    Task<Clinic?> GetClinicAsync(CancellationToken ct = default);

    Task<Guid?> GetClinicIdAsync(CancellationToken ct = default);
}

public class CurrentClinicContext : ICurrentClinicContext
{
    private readonly ApplicationDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentClinicContext(ApplicationDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<Clinic?> GetClinicAsync(CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId is null) return null;

        var membership = await _db.ClinicUsers
            .Include(cu => cu.Clinic)
            .FirstOrDefaultAsync(cu => cu.UserId == userId && cu.IsActive, ct);

        return membership?.Clinic;
    }

    public async Task<Guid?> GetClinicIdAsync(CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (userId is null) return null;

        var membership = await _db.ClinicUsers
            .Where(cu => cu.UserId == userId && cu.IsActive)
            .Select(cu => new { cu.ClinicId })
            .FirstOrDefaultAsync(ct);

        return membership?.ClinicId;
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
