using Omni.App.Business.Contracts.Managers;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contracts.Clinics;

namespace Omni.App.Business.Managers;

public class ClinicContext : IClinicContext
{
    private readonly IClinicRepository _clinics;

    public ClinicContext(IClinicRepository clinics)
    {
        _clinics = clinics;
    }

    public Task<Clinic?> GetByIdAsync(Guid clinicId, CancellationToken ct = default) => _clinics.GetReadOnlyAsync(clinicId, ct);
}
