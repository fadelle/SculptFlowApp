using System.Text.Json;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.WhatsApp;
using Omni.App.Entities.Responses.WhatsApp;
using Omni.App.Persistence.Contracts;

namespace Omni.App.Business.Engines.WhatsApp.Handlers;

/// <summary>Handles ParsedMetaEventKind.AppStateSync (change.field == "smb_app_state_sync") —
/// WhatsApp Business App/Coexistence linked-device state sync. Not a message, not health data;
/// recognized and logged distinctly from "unknown" for diagnostics, never acted on further.</summary>
public class AppStateSyncHandler
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventLogger _events;

    public AppStateSyncHandler(IUnitOfWork unitOfWork, IEventLogger events)
    {
        _unitOfWork = unitOfWork;
        _events = events;
    }

    public async Task<WhatsAppWebhookResponse> HandleAsync(ParsedMetaEvent evt, ResolvedClinic clinic, CancellationToken ct)
    {
        _events.Log(clinic.ClinicId, EventTypes.WhatsAppAppStateSyncReceived, source: "n8n",
            metadataJson: JsonSerializer.Serialize(new { raw = evt.RawJson }));
        await _unitOfWork.SaveChangesAsync(ct);

        return new WhatsAppWebhookResponse(true, "app_state_sync", false, clinic.ClinicId, clinic.ChannelIntegrationId);
    }
}
