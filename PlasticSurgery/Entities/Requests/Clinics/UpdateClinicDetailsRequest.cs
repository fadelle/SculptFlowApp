namespace PlasticSurgery.Entities.Requests.Clinics;

/// <summary>The Clinic Info → Details form.</summary>
public record UpdateClinicDetailsRequest(string? Name, string? Phone, string? Email, string? Website, string? Address,
    string? OperatingHours, string? ConsultationInfo);
