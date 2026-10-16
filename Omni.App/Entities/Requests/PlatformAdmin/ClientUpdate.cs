namespace Omni.App.Entities.Requests.PlatformAdmin;

public record ClientUpdate(string Name, string? Phone, string? Email, string? Website, string? CountryCode, string Timezone,
    string? Address, string? OperatingHours, string? ConsultationInfo);
