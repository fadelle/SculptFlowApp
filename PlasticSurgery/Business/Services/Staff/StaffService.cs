using PlasticSurgery.Business.Contracts.Services.Staff;
using PlasticSurgery.Business.Services.Clinics;
using PlasticSurgery.Entities.Responses.Staff;
using PlasticSurgery.Persistence.Contracts.Clinics;

namespace PlasticSurgery.Business.Services.Staff;

public class StaffService : IStaffService
{
    private readonly IClinicRepository _clinics;

    public StaffService(IClinicRepository clinics)
    {
        _clinics = clinics;
    }

    public async Task<IReadOnlyList<StaffMemberResponse>> ListAsync(Guid clinicId, CancellationToken ct = default)
    {
        var rows = await _clinics.ListStaffAsync(clinicId, ClinicRegistrationService.FullNameClaimType, ct);

        return rows
            .OrderByDescending(r => r.IsActive)
            .ThenBy(r => r.FullName ?? r.Email, StringComparer.OrdinalIgnoreCase)
            .Select(r => new StaffMemberResponse(r.UserId, r.FullName, r.Email, r.IsActive, r.CreatedAt))
            .ToList();
    }
}
