using PlasticSurgery.Business.Contracts.Services.Clinics;
using PlasticSurgery.Entities.Models;
using PlasticSurgery.Entities.Requests.Clinics;
using PlasticSurgery.Persistence.Contracts;
using PlasticSurgery.Persistence.Contracts.Clinics;

namespace PlasticSurgery.Business.Services.Clinics;

public class ClinicProfileService : IClinicProfileService
{
    private const int MaxNameLength = 200;

    private readonly IClinicRepository _clinics;
    private readonly IUnitOfWork _unitOfWork;

    public ClinicProfileService(IClinicRepository clinics, IUnitOfWork unitOfWork)
    {
        _clinics = clinics;
        _unitOfWork = unitOfWork;
    }

    public async Task UpdateDetailsAsync(Guid clinicId, UpdateClinicDetailsRequest request, CancellationToken ct = default)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) throw new ArgumentException("Clinic name is required.");
        if (name.Length > MaxNameLength) throw new ArgumentException($"Clinic name must be {MaxNameLength} characters or fewer.");

        var clinic = await _clinics.GetAsync(clinicId, ct) ?? throw new ArgumentException("Clinic not found.");
        clinic.Name = name;
        clinic.Phone = request.Phone;
        clinic.Email = request.Email;
        clinic.Website = request.Website;
        clinic.Address = request.Address;
        clinic.OperatingHours = request.OperatingHours;
        clinic.ConsultationInfo = request.ConsultationInfo;
        clinic.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
