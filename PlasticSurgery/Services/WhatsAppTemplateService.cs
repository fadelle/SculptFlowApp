using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PlasticSurgery.Data;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

public class WhatsAppTemplateService : IWhatsAppTemplateService
{
    private readonly ApplicationDbContext _db;
    private readonly IMetaGraphClient _meta;
    private readonly IInboxNotifier _notifier;
    private readonly IEventLogger _events;

    public WhatsAppTemplateService(ApplicationDbContext db, IMetaGraphClient meta, IInboxNotifier notifier, IEventLogger events)
    {
        _db = db;
        _meta = meta;
        _notifier = notifier;
        _events = events;
    }

    public async Task<IReadOnlyList<WhatsAppTemplateResponse>> ListAsync(Guid clinicId, CancellationToken ct = default)
    {
        var templates = await _db.WhatsAppTemplates
            .Where(t => t.ClinicId == clinicId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
        return templates.Select(ToResponse).ToList();
    }

    public async Task<WhatsAppTemplateResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var template = await _db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.ClinicId == clinicId && t.Id == id, ct);
        return template is null ? null : ToResponse(template);
    }

    public async Task<WhatsAppTemplateResponse> CreateAsync(CreateWhatsAppTemplateRequest request, CancellationToken ct = default)
    {
        if (!WhatsAppTemplateCategory.All.Contains(request.Category))
        {
            throw new ArgumentException($"Invalid category '{request.Category}'.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Body))
        {
            throw new ArgumentException("Name and Body are required.", nameof(request));
        }

        var now = DateTimeOffset.UtcNow;
        var template = new WhatsAppTemplate
        {
            Id = Guid.NewGuid(),
            ClinicId = request.ClinicId,
            Name = request.Name,
            Category = request.Category,
            Language = string.IsNullOrWhiteSpace(request.Language) ? "en_US" : request.Language,
            Status = WhatsAppTemplateStatus.Draft,
            HeaderType = request.HeaderType,
            HeaderContent = request.HeaderContent,
            Body = request.Body,
            Footer = request.Footer,
            ButtonsJson = request.ButtonsJson,
            VariablesJson = request.VariablesJson,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.WhatsAppTemplates.Add(template);
        await _db.SaveChangesAsync(ct);

        var integration = await _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.ClinicId == request.ClinicId && c.Channel == ChannelType.WhatsApp, ct);

        if (integration is null || integration.Status != ChannelIntegrationStatus.Connected
            || string.IsNullOrEmpty(integration.WhatsAppBusinessId) || string.IsNullOrEmpty(integration.AccessToken))
        {
            // Saved as a local draft — fine to submit later (re-run CreateAsync's Meta call via a
            // retry action, not yet wired up) once the clinic finishes connecting WhatsApp under
            // Settings → Channels & Integrations.
            return ToResponse(template);
        }

        try
        {
            var components = BuildMetaComponents(template);
            var result = await _meta.CreateMessageTemplateAsync(
                integration.WhatsAppBusinessId, integration.AccessToken,
                template.Name, template.Category, template.Language, components, ct);

            template.MetaTemplateId = result.Id;
            template.Status = MapMetaStatus(result.Status);
            template.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        catch (MetaGraphApiException ex)
        {
            // Keep the draft row (so the user doesn't lose their work) but surface Meta's rejection.
            template.Status = WhatsAppTemplateStatus.Rejected;
            template.RejectionReason = ex.Message;
            template.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return ToResponse(template);
    }

    public async Task<WhatsAppTemplateResponse?> SyncStatusAsync(Guid clinicId, Guid id, CancellationToken ct = default)
    {
        var template = await _db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.ClinicId == clinicId && t.Id == id, ct);
        if (template is null) return null;
        if (string.IsNullOrEmpty(template.MetaTemplateId))
        {
            // Never successfully submitted to Meta — nothing to sync yet.
            return ToResponse(template);
        }

        var integration = await _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.ClinicId == clinicId && c.Channel == ChannelType.WhatsApp, ct);
        if (integration is null || string.IsNullOrEmpty(integration.AccessToken))
        {
            throw new InvalidOperationException("This clinic doesn't have a connected WhatsApp number to sync against.");
        }

        var result = await _meta.GetMessageTemplateStatusAsync(template.MetaTemplateId, integration.AccessToken, ct);
        template.Status = MapMetaStatus(result.Status);
        template.RejectionReason = result.RejectedReason;
        template.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ToResponse(template);
    }

    public async Task<WhatsAppTemplateResponse> ApplyMetaEventAsync(WhatsAppTemplateEventRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Name is required (used as the upsert fallback key when MetaTemplateId is absent).", nameof(request));
        }

        // Don't trust ClinicId blindly — if the event carries a WabaId, it must match this
        // clinic's own stored WhatsApp connection, or a misrouted/misconfigured n8n workflow could
        // write another clinic's template data under this clinic's id.
        ChannelIntegration? integration = null;
        if (!string.IsNullOrWhiteSpace(request.WabaId))
        {
            integration = await _db.ChannelIntegrations.FirstOrDefaultAsync(
                c => c.ClinicId == request.ClinicId && c.Channel == ChannelType.WhatsApp, ct);
            if (integration is not null && !string.Equals(integration.WhatsAppBusinessId, request.WabaId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"WabaId '{request.WabaId}' does not match clinic {request.ClinicId}'s stored WhatsApp connection.");
            }
        }
        integration ??= await _db.ChannelIntegrations.FirstOrDefaultAsync(
            c => c.ClinicId == request.ClinicId && c.Channel == ChannelType.WhatsApp, ct);

        var template = !string.IsNullOrWhiteSpace(request.MetaTemplateId)
            ? await _db.WhatsAppTemplates.FirstOrDefaultAsync(
                t => t.ClinicId == request.ClinicId && t.MetaTemplateId == request.MetaTemplateId, ct)
            : null;

        template ??= await _db.WhatsAppTemplates.FirstOrDefaultAsync(
            t => t.ClinicId == request.ClinicId && t.Name == request.Name
                && t.Language == (request.Language ?? "en_US"), ct);

        var now = DateTimeOffset.UtcNow;
        var previousStatus = template?.Status;

        if (template is null)
        {
            // Meta knows about a template we don't have a local row for yet (created directly in
            // Business Manager, or our own create-template call never completed) — create a
            // minimal row from what the event tells us rather than dropping the event.
            template = new WhatsAppTemplate
            {
                Id = Guid.NewGuid(),
                ClinicId = request.ClinicId,
                Name = request.Name,
                Language = request.Language ?? "en_US",
                Category = (request.Category ?? WhatsAppTemplateCategory.Utility).ToLowerInvariant(),
                Body = string.Empty,
                CreatedAt = now
            };
            _db.WhatsAppTemplates.Add(template);
        }

        if (!string.IsNullOrWhiteSpace(request.MetaTemplateId)) template.MetaTemplateId = request.MetaTemplateId;
        if (!string.IsNullOrWhiteSpace(request.Status)) template.Status = MapMetaStatus(request.Status);
        if (!string.IsNullOrWhiteSpace(request.Category)) template.Category = request.Category.ToLowerInvariant();
        if (request.PreviousCategory is not null) template.PreviousCategory = request.PreviousCategory;
        if (request.CurrentCategory is not null) template.CurrentCategory = request.CurrentCategory;
        if (request.QualityRating is not null) template.QualityRating = request.QualityRating;
        if (request.ComponentsJson is not null) template.ComponentsJson = request.ComponentsJson;
        if (request.Reason is not null) template.RejectionReason = request.Reason;
        template.ChannelIntegrationId = integration?.Id ?? template.ChannelIntegrationId;
        template.LastMetaEventAt = request.OccurredAt ?? now;
        template.UpdatedAt = now;

        _events.Log(request.ClinicId, EventTypes.WhatsAppTemplateStatusChanged, source: "n8n",
            metadataJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                templateId = template.Id, eventType = request.EventType,
                previousStatus, newStatus = template.Status
            }));

        await _db.SaveChangesAsync(ct);

        await _notifier.WhatsAppTemplateUpdatedAsync(
            request.ClinicId, template.Id, template.Name, template.Status, template.RejectionReason, now, ct);

        return ToResponse(template);
    }

    /// <summary>Builds Meta's "components" array (header/body/footer/buttons) for a template create
    /// call from our stored fields. {{1}}, {{2}}... placeholders in Body pass through as-is — Meta
    /// parses those itself from the text.</summary>
    private static object[] BuildMetaComponents(WhatsAppTemplate template)
    {
        var components = new List<object>();

        if (!string.IsNullOrWhiteSpace(template.HeaderType) && template.HeaderType != WhatsAppTemplateHeaderType.None)
        {
            components.Add(template.HeaderType == WhatsAppTemplateHeaderType.Text
                ? new { type = "HEADER", format = "TEXT", text = template.HeaderContent ?? string.Empty }
                : new { type = "HEADER", format = template.HeaderType!.ToUpperInvariant() });
        }

        components.Add(new { type = "BODY", text = template.Body });

        if (!string.IsNullOrWhiteSpace(template.Footer))
        {
            components.Add(new { type = "FOOTER", text = template.Footer });
        }

        if (!string.IsNullOrWhiteSpace(template.ButtonsJson))
        {
            using var doc = JsonDocument.Parse(template.ButtonsJson);
            components.Add(new { type = "BUTTONS", buttons = JsonSerializer.Deserialize<object[]>(doc.RootElement.GetRawText())! });
        }

        return components.ToArray();
    }

    /// <summary>Normalizes Meta's known statuses to our lowercase constants; anything unrecognized
    /// passes through lowercased rather than being collapsed to "pending" — tolerating a status
    /// Meta introduces later (e.g. "in_appeal") is the explicit requirement here, not a bug.</summary>
    private static string MapMetaStatus(string metaStatus) => metaStatus.ToUpperInvariant() switch
    {
        "PENDING" => WhatsAppTemplateStatus.Pending,
        "APPROVED" => WhatsAppTemplateStatus.Approved,
        "REJECTED" => WhatsAppTemplateStatus.Rejected,
        "PAUSED" => WhatsAppTemplateStatus.Paused,
        "DISABLED" => WhatsAppTemplateStatus.Disabled,
        "FLAGGED" => WhatsAppTemplateStatus.Flagged,
        "DELETED" => WhatsAppTemplateStatus.Deleted,
        _ => metaStatus.ToLowerInvariant()
    };

    private static WhatsAppTemplateResponse ToResponse(WhatsAppTemplate t) => new(
        t.Id, t.ClinicId, t.MetaTemplateId, t.Name, t.Category, t.Language, t.Status,
        t.HeaderType, t.HeaderContent, t.Body, t.Footer, t.ButtonsJson, t.VariablesJson,
        t.RejectionReason, t.CreatedAt, t.UpdatedAt,
        t.ChannelIntegrationId, t.QualityRating, t.PreviousCategory, t.CurrentCategory,
        t.ComponentsJson, t.LastMetaEventAt
    );
}
