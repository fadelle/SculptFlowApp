using PlasticSurgery.Dtos;

namespace PlasticSurgery.Services;

/// <summary>
/// Manages WhatsApp message templates: local drafts, submission to Meta for review, and syncing
/// review status back from Meta. Templates are the only thing Meta allows sending once a
/// conversation's 24-hour customer service window has closed — see Conversation.IsServiceWindowOpen.
/// Actual sending (Inbox reply, campaign) goes through IMessageService/IWhatsAppService instead;
/// this service only owns template lifecycle.
/// </summary>
public interface IWhatsAppTemplateService
{
    Task<IReadOnlyList<WhatsAppTemplateResponse>> ListAsync(Guid clinicId, CancellationToken ct = default);

    Task<WhatsAppTemplateResponse?> GetByIdAsync(Guid clinicId, Guid id, CancellationToken ct = default);

    /// <summary>Persists a draft, then immediately submits it to Meta for review. The saved row's
    /// Status reflects whatever Meta returned (usually "pending") even if submission fails partway —
    /// callers can always retry via SyncStatusAsync once the underlying WABA config issue is fixed.</summary>
    Task<WhatsAppTemplateResponse> CreateAsync(CreateWhatsAppTemplateRequest request, CancellationToken ct = default);

    /// <summary>Refreshes Status/RejectionReason from Meta for a template that's already been submitted.</summary>
    Task<WhatsAppTemplateResponse?> SyncStatusAsync(Guid clinicId, Guid id, CancellationToken ct = default);

    /// <summary>Applies a normalized template webhook event n8n forwarded (status change, category
    /// change, quality update) — upserts by (ClinicId, MetaTemplateId), falling back to
    /// (ClinicId, Name, Language) for events that don't carry Meta's id yet. Idempotent by
    /// construction: re-applying the same state twice just re-sets the same fields, no duplicate
    /// rows possible. Broadcasts WhatsAppTemplateUpdated on success.</summary>
    Task<WhatsAppTemplateResponse> ApplyMetaEventAsync(WhatsAppTemplateEventRequest request, CancellationToken ct = default);
}
