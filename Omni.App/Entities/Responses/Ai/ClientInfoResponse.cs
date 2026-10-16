namespace Omni.App.Entities.Responses.Ai;

/// <summary>Response for GET /api/ai/client-info — general info the AI agent answers questions
/// from directly instead of inventing it. Address/OperatingHours/BookingInfo are free text
/// (see Client entity), matching this MVP's level of structure elsewhere.</summary>
public record ClientInfoResponse(
    Guid ClientId,
    string Name,
    string? Phone,
    string? Email,
    string? Website,
    string? Address,
    string? OperatingHours,
    string? BookingInfo,
    string Timezone
);
