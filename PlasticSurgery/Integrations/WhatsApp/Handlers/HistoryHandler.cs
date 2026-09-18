using System.Text.Json;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Integrations.WhatsApp.Models;
using PlasticSurgery.Services;

namespace PlasticSurgery.Integrations.WhatsApp.Handlers;

/// <summary>Handles ParsedMetaEventKind.HistorySync (change.field == "history") — Coexistence
/// history backfill, not live traffic. This app doesn't import historical threads yet, so this
/// just logs the event as recognized (distinct from "unknown") and acknowledges. Deliberately never
/// creates Messages or touches a Conversation — a backfilled historical message must never be
/// mistaken for a new live customer message or reach the AI.</summary>
public class HistoryHandler
{
    private readonly ApplicationDbContext _db;
    private readonly IEventLogger _events;

    public HistoryHandler(ApplicationDbContext db, IEventLogger events)
    {
        _db = db;
        _events = events;
    }

    public async Task<WhatsAppWebhookResponse> HandleAsync(ParsedMetaEvent evt, ResolvedClinic clinic, CancellationToken ct)
    {
        _events.Log(clinic.ClinicId, EventTypes.WhatsAppHistorySyncReceived, source: "n8n",
            metadataJson: JsonSerializer.Serialize(new { raw = evt.RawJson }));
        await _db.SaveChangesAsync(ct);

        return new WhatsAppWebhookResponse(true, "history", false, clinic.ClinicId, clinic.ChannelIntegrationId);
    }
}
