using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

/// <summary>
/// Owns a clinic's WhatsApp account/phone-number health — the current state (on ChannelIntegration,
/// which doubles as "the clinic's WhatsApp connection") plus the append-only history behind it
/// (WhatsAppHealthEvent). Simplifies Meta's raw status vocabulary into a small HealthLevel the
/// dashboard can show without exposing webhook internals to clinic staff.
/// </summary>
public interface IWhatsAppHealthService
{
    Task<WhatsAppHealthResponse?> GetHealthAsync(Guid clinicId, CancellationToken ct = default);

    Task<IReadOnlyList<WhatsAppHealthEventResponse>> GetHealthEventsAsync(
        Guid clinicId, int skip, int take, CancellationToken ct = default);

    /// <summary>Applies a normalized health webhook event n8n forwarded, updates the clinic's
    /// WhatsApp connection health fields, appends a history row, and broadcasts
    /// WhatsAppHealthUpdated. Idempotent for retried deliveries of the same event (see the unique
    /// index behind WhatsAppHealthEvent).</summary>
    Task<WhatsAppHealthResponse> ApplyHealthEventAsync(WhatsAppHealthEventRequest request, CancellationToken ct = default);
}
