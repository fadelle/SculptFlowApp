namespace PlasticSurgery.Entities.Dtos.PlatformAdmin;

/// <summary>One clinic for the admin portal.</summary>
public record ClinicDetail(Guid Id, string Name, string Slug, string? Phone, string? Email, string? Website, string? CountryCode,
    string Timezone, string? Address, string? OperatingHours, string? ConsultationInfo, bool IsActive, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
