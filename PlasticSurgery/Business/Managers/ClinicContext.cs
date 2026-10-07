using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Persistence.Contracts.Clinics;

namespace PlasticSurgery.Business.Managers;

public class ClinicContext : IClinicContext
{
    private readonly IClinicRepository _clinics;

    public ClinicContext(IClinicRepository clinics)
    {
        _clinics = clinics;
    }

    public Task<Clinic?> GetByIdAsync(Guid clinicId, CancellationToken ct = default) => _clinics.GetAsync(clinicId, ct);
}
