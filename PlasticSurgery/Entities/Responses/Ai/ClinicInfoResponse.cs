namespace PlasticSurgery.Entities.Responses.Ai;

/// <summary>Response for GET /api/ai/clinic-info — general info the AI agent answers questions
/// from directly instead of inventing it. Address/OperatingHours/ConsultationInfo are free text
/// (see Clinic entity), matching this MVP's level of structure elsewhere.</summary>
public record ClinicInfoResponse(
    Guid ClinicId,
    string Name,
    string? Phone,
    string? Email,
    string? Website,
    string? Address,
    string? OperatingHours,
    string? ConsultationInfo,
    string Timezone
);
