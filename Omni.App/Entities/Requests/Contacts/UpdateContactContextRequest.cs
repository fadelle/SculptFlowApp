namespace Omni.App.Entities.Requests.Contacts;

/// <summary>Body for the AI assistant's update_contact tool (POST /api/ai/contacts/{id}) — deliberately
/// narrower than UpdateContactRequest: it excludes identity fields (FullName/Phone/Email), which come
/// from the contact's actual source (webhook/intake), not the AI's judgment. Only null fields are left
/// unchanged — same partial-update semantics as UpdateContactRequest. Stage is a stage key of the client's business type
/// or a stage category (open, booked, attended, no_show, converted, closed_lost, needs_human; the server picks the
/// category's stage), so the AI never needs to know one type's keys.</summary>
public record UpdateContactContextRequest(
    Guid? ServiceId,
    string? PreferredLanguage,
    string? City,
    string? DesiredTimeline,
    string? Notes,
    DateTimeOffset? NextFollowupAt,
    string? Qualification,
    string? Stage = null
);
