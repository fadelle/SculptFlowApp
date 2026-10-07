namespace PlasticSurgery.Entities.Requests.PlatformAdmin;

public record ClinicUpdate(string Name, string? Phone, string? Email, string? Website, string? CountryCode, string Timezone,
    string? Address, string? OperatingHours, string? ConsultationInfo);
