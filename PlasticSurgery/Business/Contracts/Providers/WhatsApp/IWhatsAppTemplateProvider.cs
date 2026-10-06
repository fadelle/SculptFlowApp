using PlasticSurgery.Entities.Dtos.WhatsApp;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Contracts.Providers.WhatsApp;

/// <summary>
/// Template review for one WhatsApp provider — the template-management half of IWhatsAppProvider. Picked by the
/// same global WhatsApp:Provider switch, so the Templates page works unchanged on either provider:
///   meta    -> Business/Providers/WhatsApp/MetaWhatsAppTemplateProvider.cs  (Graph /{waba-id}/message_templates)
///   infobip -> Business/Providers/WhatsApp/InfobipWhatsAppTemplateProvider.cs (/whatsapp/2/senders/{sender}/templates)
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
