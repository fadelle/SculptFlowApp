using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Services;

/// <summary>
/// Two narrow remaining jobs now that dashboard pages/APIs resolve clinic via
/// ICurrentClinicContext (clinic_users, tied to the logged-in Identity user) instead:
///   - GetDefaultClinicAsync: this MVP has no clinic-creation/signup wizard, so new-user
///     Registration links every new account to the single clinic named by the "Clinic:DefaultSlug"
///     config key — see Pages/Account/Register.cshtml.cs, its only remaining caller.
///   - GetByIdAsync: used by the trusted server-to-server surfaces that still take clinicId
///     explicitly rather than from a logged-in session — the AI agent tool endpoints
///     (Controllers/AiController.cs) and the WhatsApp webhook path, which resolve clinic from
///     Meta's WABA/phone_number_id, not from a browser session at all.
/// </summary>
public interface IClinicContext
{
    Task<Clinic?> GetDefaultClinicAsync(CancellationToken ct = default);

    /// <summary>Looks up a clinic by id — used by clinicId-scoped API endpoints (the AI agent tool
    /// surface, campaigns, etc.) that already have the id explicit rather than needing the
    /// "default clinic" placeholder resolution.</summary>
    Task<Clinic?> GetByIdAsync(Guid clinicId, CancellationToken ct = default);
}

public class ClinicContext : IClinicContext
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;

    public ClinicContext(ApplicationDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<Clinic?> GetDefaultClinicAsync(CancellationToken ct = default)
    {
        var slug = _configuration["Clinic:DefaultSlug"] ?? "demo-clinic";
        return await _db.Clinics.FirstOrDefaultAsync(c => c.Slug == slug, ct);
    }

    public async Task<Clinic?> GetByIdAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, ct);
}
