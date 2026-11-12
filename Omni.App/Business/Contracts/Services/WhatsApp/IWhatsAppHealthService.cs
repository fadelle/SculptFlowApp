using Omni.App.Entities.Models;
using Omni.App.Common.Exceptions;
using Omni.App.Entities.Requests.WhatsApp;
using Omni.App.Entities.Responses.WhatsApp;

namespace Omni.App.Business.Contracts.Services.WhatsApp;

/// <summary>
/// Owns a client's WhatsApp account/phone-number health — the current state (on ChannelIntegration,
/// which doubles as "the client's WhatsApp connection") plus the append-only history behind it
/// (WhatsAppHealthEvent). Simplifies Meta's raw status vocabulary into a small HealthLevel the
/// dashboard can show without exposing webhook internals to client members.
/// </summary>
public interface IWhatsAppHealthService
{
    Task<WhatsAppHealthResponse?> GetHealthAsync(Guid clientId, CancellationToken ct = default);

    Task<IReadOnlyList<WhatsAppHealthEventResponse>> GetHealthEventsAsync(
        Guid clientId, int skip, int take, CancellationToken ct = default);

    /// <summary>Applies a normalized health webhook event n8n forwarded, updates the client's
    /// WhatsApp connection health fields, appends a history row, and broadcasts
    /// WhatsAppHealthUpdated. Idempotent for retried deliveries of the same event (see the unique
    /// index behind WhatsAppHealthEvent).</summary>
    Task<WhatsAppHealthResponse> ApplyHealthEventAsync(WhatsAppHealthEventRequest request, CancellationToken ct = default);

    /// <summary>A send failed because the connection is broken (WhatsAppSendException.ConnectionProblemCode; QA t8-10: an expired
    /// token left the number "Healthy" while every message failed): health becomes a problem with Meta's message, open Health
    /// pages update, and the team gets one notification when it turns unhealthy. Anything else is ignored.</summary>
    Task RecordSendFailureAsync(ChannelIntegration integration, WhatsAppSendException failure, CancellationToken ct = default);
}
