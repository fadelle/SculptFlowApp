using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlasticSurgery.Dtos;

/// <summary>Optional filters for Campaign.AudienceFilters (jsonb). Deliberately a small fixed set
/// of fields, not a generic query language — see CampaignAudienceService for how each is applied.
/// Every field is optional; an absent field means "don't filter on this." InactiveDays/ProcedureId/
/// Sources are used by both the reactivation and custom audiences; everything below Sources is
/// "advanced" — offered only in the custom-audience builder's progressive-disclosure section, per
/// the Campaign creation UI (see wwwroot/js/campaign-audience.js). Every field maps to an existing
/// Lead/Appointment column (QualificationStatus, CreatedAt, LastContactAt, CountryCode, City,
/// Appointment.Status) — no schema change, just more ways to slice data that already exists.</summary>
public record CampaignAudienceFilters(
    [property: JsonPropertyName("inactiveDays")] int? InactiveDays = null,
    [property: JsonPropertyName("procedureId")] Guid? ProcedureId = null,
    [property: JsonPropertyName("leadStatuses")] IReadOnlyList<string>? LeadStatuses = null,
    [property: JsonPropertyName("sources")] IReadOnlyList<string>? Sources = null,
    [property: JsonPropertyName("qualificationStatuses")] IReadOnlyList<string>? QualificationStatuses = null,
    [property: JsonPropertyName("createdAfter")] DateTimeOffset? CreatedAfter = null,
    [property: JsonPropertyName("createdBefore")] DateTimeOffset? CreatedBefore = null,
    [property: JsonPropertyName("lastContactedAfter")] DateTimeOffset? LastContactedAfter = null,
    [property: JsonPropertyName("lastContactedBefore")] DateTimeOffset? LastContactedBefore = null,
    [property: JsonPropertyName("appointmentStatuses")] IReadOnlyList<string>? AppointmentStatuses = null,
    [property: JsonPropertyName("countries")] IReadOnlyList<string>? Countries = null,
    [property: JsonPropertyName("cities")] IReadOnlyList<string>? Cities = null
)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static CampaignAudienceFilters Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new CampaignAudienceFilters();
        try
        {
            return JsonSerializer.Deserialize<CampaignAudienceFilters>(json, Options) ?? new CampaignAudienceFilters();
        }
        catch (JsonException)
        {
            // Malformed filters JSON shouldn't crash audience resolution — fall back to "no filters" (mandatory exclusions still apply).
            return new CampaignAudienceFilters();
        }
    }
}
