namespace Omni.App.Entities.Models;

public class Client
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? CountryCode { get; set; }
    public string Timezone { get; set; } = "UTC";

    /// <summary>Free-text fields the AI agent's get_client_info tool reads to answer general
    /// questions (location, hours, consultation policy) — plain text, not a structured schedule,
    /// matching this MVP's level of the rest of Client. Edited under Settings → Business Info.</summary>
    public string? Address { get; set; }
    public string? OperatingHours { get; set; }
    public string? ConsultationInfo { get; set; }

    /// <summary>core.business_types.code: the words, stages and AI profile this client uses. Chosen at sign-up; changed
    /// only by a platform admin (PUT /api/platform-admin/clients/{id}/business-type), which also moves contacts' stages.</summary>
    public string BusinessTypeCode { get; set; } = string.Empty;

    /// <summary>The language the dashboard opens in when the user has none of their own (<see cref="Common.Enums.AppLanguage"/>).</summary>
    public string DefaultLanguage { get; set; } = Common.Enums.AppLanguage.English;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<Procedure> Procedures { get; set; } = new List<Procedure>();
    public ICollection<Lead> Leads { get; set; } = new List<Lead>();
}
