using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

/// <summary>Who has access to a clinic. Read-only for now: the list of users linked to the clinic through
/// clinic_users. Always scoped by the clinicId the caller resolved from CurrentClinicContext.</summary>
public interface IStaffService
{
    /// <summary>Members of this clinic only — active first, then by name/email. Full name comes from the
    /// Identity "full_name" claim set at registration (null for accounts created before that existed).</summary>
    Task<IReadOnlyList<StaffMemberResponse>> ListAsync(Guid clinicId, CancellationToken ct = default);
}

public class StaffService : IStaffService
{
    private readonly ApplicationDbContext _db;

    public StaffService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<StaffMemberResponse>> ListAsync(Guid clinicId, CancellationToken ct = default)
    {
        var rows = await (
            from cu in _db.ClinicUsers
            where cu.ClinicId == clinicId
            join u in _db.Users on cu.UserId equals u.Id
            select new
            {
                cu.UserId,
                u.Email,
                cu.IsActive,
                cu.CreatedAt,
                FullName = _db.UserClaims
                    .Where(c => c.UserId == u.Id && c.ClaimType == ClinicRegistrationService.FullNameClaimType)
                    .Select(c => c.ClaimValue)
                    .FirstOrDefault()
            }).ToListAsync(ct);

        return rows
            .OrderByDescending(r => r.IsActive)
            .ThenBy(r => r.FullName ?? r.Email, StringComparer.OrdinalIgnoreCase)
            .Select(r => new StaffMemberResponse(r.UserId, r.FullName, r.Email, r.IsActive, r.CreatedAt))
            .ToList();
    }
}
