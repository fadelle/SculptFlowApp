namespace PlasticSurgery.Data.Entities;

public class Clinic
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? CountryCode { get; set; }
    public string Timezone { get; set; } = "UTC";

    /// <summary>Free-text fields the AI agent's get_clinic_info tool reads to answer general
    /// questions (location, hours, consultation policy) — plain text, not a structured schedule,
    /// matching this MVP's level of the rest of Clinic. Edited under Settings → Clinic Info.</summary>
    public string? Address { get; set; }
    public string? OperatingHours { get; set; }
    public string? ConsultationInfo { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<Procedure> Procedures { get; set; } = new List<Procedure>();
    public ICollection<Lead> Leads { get; set; } = new List<Lead>();
}
