using System.Text.Json;
using PlasticSurgery.Business.Contracts.Managers;
using PlasticSurgery.Common.Statics;
using PlasticSurgery.Entities.Dtos.WhatsApp;
using PlasticSurgery.Entities.Responses.WhatsApp;
using PlasticSurgery.Persistence.Contracts;

namespace PlasticSurgery.Business.Engines.WhatsApp.Handlers;

/// <summary>Handles ParsedMetaEventKind.Unknown — a Meta webhook field this app doesn't (yet)
/// understand. Logs the raw field name and payload for later diagnosis and acknowledges
/// successfully; never throws, never retried, never reaches the AI. IEventLogger.Log doesn't call
/// SaveChanges itself (see its doc comment), so this handler commits its own — nothing else in this
/// event's path does.</summary>
public class UnknownEventHandler
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventLogger _events;

    public UnknownEventHandler(IUnitOfWork unitOfWork, IEventLogger events)
    {
        _unitOfWork = unitOfWork;
        _events = events;
    }

    public async Task<WhatsAppWebhookResponse> HandleAsync(ParsedMetaEvent evt, ResolvedClinic? clinic, CancellationToken ct)
    {
        if (clinic is not null)
        {
            _events.Log(clinic.ClinicId, EventTypes.WhatsAppUnknownEventReceived, source: "n8n",
                metadataJson: JsonSerializer.Serialize(new { field = evt.RawFieldName, raw = evt.RawJson }));
            await _unitOfWork.SaveChangesAsync(ct);
        }

        return new WhatsAppWebhookResponse(true, "unknown", false, clinic?.ClinicId, clinic?.ChannelIntegrationId);
    }
}
