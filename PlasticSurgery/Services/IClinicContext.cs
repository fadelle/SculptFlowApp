using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Services;

/// <summary>
/// Looks up a clinic by id — used by the trusted server-to-server surfaces that take clinicId
/// explicitly rather than from a logged-in session: the AI agent tool endpoints
/// (Controllers/AiController.cs) and the WhatsApp webhook path, which resolve clinic from Meta's
/// WABA/phone_number_id, not from a browser session at all.
///
/// (There used to be a "default clinic" lookup here, driven by the Clinic:DefaultSlug config key, that
/// registration used to attach every new account to one shared clinic. Registration now creates a
/// new clinic per signup — see ClinicRegistrationService — so that lookup is gone.)
/// </summary>
public interface IClinicContext
{
    /// <summary>Looks up a clinic by id — used by clinicId-scoped API endpoints (the AI agent tool
    /// surface, campaigns, etc.) that already have the id explicit.</summary>
    Task<Clinic?> GetByIdAsync(Guid clinicId, CancellationToken ct = default);
}

public class ClinicContext : IClinicContext
{
    private readonly ApplicationDbContext _db;

    public ClinicContext(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Clinic?> GetByIdAsync(Guid clinicId, CancellationToken ct = default) =>
        await _db.Clinics.FirstOrDefaultAsync(c => c.Id == clinicId, ct);
}
