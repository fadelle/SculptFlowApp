namespace PlasticSurgery.Entities.Requests.Leads;

/// <summary>Body for the AI agent's update_lead tool (POST /api/ai/leads/{id}) — deliberately
/// narrower than UpdateLeadRequest: it excludes identity fields (FullName/Phone/Email), which come
/// from the lead's actual source (webhook/intake), not the AI's judgment. Only null fields are left
/// unchanged — same partial-update semantics as UpdateLeadRequest.</summary>
public record UpdateLeadContextRequest(
    Guid? ProcedureId,
    string? PreferredLanguage,
    string? City,
    string? DesiredTimeline,
    string? Notes,
    DateTimeOffset? NextFollowupAt,
    string? QualificationStatus
);
