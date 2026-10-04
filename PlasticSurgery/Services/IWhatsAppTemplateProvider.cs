using PlasticSurgery.Data.Entities;

namespace PlasticSurgery.Services;

/// <summary>A provider's answer to a template submit. Status is in WhatsApp's own vocabulary (PENDING,
/// APPROVED, REJECTED, PAUSED, ...) — WhatsAppTemplateService maps it to our lowercase constants.</summary>
public record WhatsAppTemplateSubmitResult(string ProviderTemplateId, string Status);

/// <summary>A provider's current view of a submitted template, same vocabulary as above.</summary>
public record WhatsAppTemplateStatusResult(string Status, string? RejectedReason);

/// <summary>
/// Template review for one WhatsApp provider — the template-management half of IWhatsAppProvider. Picked by the
/// same global WhatsApp:Provider switch, so the Templates page works unchanged on either provider:
///   meta    -> Integrations/WhatsApp/MetaWhatsAppTemplateProvider.cs  (Graph /{waba-id}/message_templates)
///   infobip -> Integrations/Infobip/InfobipWhatsAppTemplateProvider.cs (/whatsapp/2/senders/{sender}/templates)
/// Status changes after submit arrive by webhook (each provider's own) and land in
/// IWhatsAppTemplateService.ApplyMetaEventAsync; Sync calls GetStatusAsync.
/// </summary>
public interface IWhatsAppTemplateProvider
{
    /// <summary>One of <see cref="ChannelProvider"/>.</summary>
    string Name { get; }

    /// <summary>True when the clinic's row has what this provider needs to manage templates.</summary>
    bool IsReady(ChannelIntegration integration);

    /// <summary>Submits a new template for WhatsApp review. Throws WhatsAppTemplateProviderException when the
    /// provider refuses it; its message is stored as the template's rejection reason, so it's shown to staff.</summary>
    Task<WhatsAppTemplateSubmitResult> SubmitAsync(ChannelIntegration integration, WhatsAppTemplate template, CancellationToken ct = default);

    Task<WhatsAppTemplateStatusResult> GetStatusAsync(ChannelIntegration integration, WhatsAppTemplate template, CancellationToken ct = default);
}

/// <summary>The provider refused a template. Message is shown to clinic staff, so it must be provider-neutral
/// (it never names Infobip — see the white-label rule in PROJECT_HANDOFF.md §29).</summary>
public class WhatsAppTemplateProviderException : Exception
{
    public WhatsAppTemplateProviderException(string message) : base(message) { }
}
