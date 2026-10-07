using PlasticSurgery.Entities.Requests.Clinics;

namespace PlasticSurgery.Business.Contracts.Services.Clinics;

/// <summary>The clinic's own details (name, contact info, opening-hours text) edited on Clinic Info.</summary>
public interface IClinicProfileService
{
    /// <summary>Throws ArgumentException (with a message for the form) when the name is missing or too long.</summary>
    Task UpdateDetailsAsync(Guid clinicId, UpdateClinicDetailsRequest request, CancellationToken ct = default);
}
