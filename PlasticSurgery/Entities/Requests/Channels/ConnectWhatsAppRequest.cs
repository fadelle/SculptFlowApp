namespace PlasticSurgery.Entities.Requests.Channels;

/// <summary>
/// Sent by the browser after the WhatsApp login flow completes. Uses a full-page OAuth redirect
/// (Code + RedirectUri — RedirectUri must exactly match the URL used in the initial authorize
/// request) rather than the JS SDK popup, since the popup's code can't be exchanged server-side
/// (Meta associates it with an internal redirect_uri we have no way to reproduce). WabaId/
/// PhoneNumberId are optional — the full-page redirect has no popup to deliver them via
/// postMessage, so when omitted the backend auto-discovers them via the Graph API instead
/// (see ChannelIntegrationService.ConnectWhatsAppAsync).
/// </summary>
public record ConnectWhatsAppRequest(
    Guid ClinicId,
    string? Code = null,
    string? AccessToken = null,
    string? RedirectUri = null,
    string? WabaId = null,
    string? PhoneNumberId = null
);
