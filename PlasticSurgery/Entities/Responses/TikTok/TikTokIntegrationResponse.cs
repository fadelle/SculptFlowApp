namespace PlasticSurgery.Entities.Responses.TikTok;

/// <summary>Settings → Channels &amp; Integrations' TikTok card. Always returned even if the clinic never
/// connected TikTok (Id = Guid.Empty, Status = disconnected) — same convention as ChannelIntegrationResponse/
/// CalendarIntegrationResponse, so the page never has to special-case "no row yet". Never carries a token.</summary>
public record TikTokIntegrationResponse(
    Guid Id,
    Guid ClinicId,
    string Status,
    string? DisplayName,
    string? AvatarUrl,
    bool IsHealthy,
    string? LastProblemMessage
);
