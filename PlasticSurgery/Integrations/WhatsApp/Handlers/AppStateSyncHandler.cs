using System.Text.Json;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Integrations.WhatsApp.Models;
using PlasticSurgery.Services;

namespace PlasticSurgery.Integrations.WhatsApp.Handlers;

/// <summary>Handles ParsedMetaEventKind.AppStateSync (change.field == "smb_app_state_sync") —
/// WhatsApp Business App/Coexistence linked-device state sync. Not a message, not health data;
/// recognized and logged distinctly from "unknown" for diagnostics, never acted on further.</summary>
public class AppStateSyncHandler
{
    private readonly ApplicationDbContext _db;
    private readonly IEventLogger _events;

    public AppStateSyncHandler(ApplicationDbContext db, IEventLogger events)
    {
        _db = db;
        _events = events;
    }

    public async Task<WhatsAppWebhookResponse> HandleAsync(ParsedMetaEvent evt, ResolvedClinic clinic, CancellationToken ct)
    {
        _events.Log(clinic.ClinicId, EventTypes.WhatsAppAppStateSyncReceived, source: "n8n",
            metadataJson: JsonSerializer.Serialize(new { raw = evt.RawJson }));
        await _db.SaveChangesAsync(ct);

        return new WhatsAppWebhookResponse(true, "app_state_sync", false, clinic.ClinicId, clinic.ChannelIntegrationId);
    }
}
