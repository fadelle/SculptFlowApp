using Omni.App.Entities.Dtos.WhatsApp;
using Omni.App.Entities.Models;

namespace Omni.App.Business.Contracts.Providers.WhatsApp;

/// <summary>
/// Template review with Meta (Graph /{waba-id}/message_templates), implemented by MetaWhatsAppTemplateProvider.
/// Status changes after submit arrive by Meta's webhook and land in IWhatsAppTemplateService.ApplyMetaEventAsync;
/// Sync calls GetStatusAsync.
/// </summary>
public interface IWhatsAppTemplateProvider
{
    /// <summary>True when the client's row has what template management needs (WABA id + access token).</summary>
    bool IsReady(ChannelIntegration integration);

    /// <summary>Submits a new template for WhatsApp review. Throws WhatsAppTemplateProviderException when Meta
    /// refuses it; its message is stored as the template's rejection reason, so it's shown to members.</summary>
    Task<WhatsAppTemplateSubmitResult> SubmitAsync(ChannelIntegration integration, WhatsAppTemplate template, CancellationToken ct = default);

    Task<WhatsAppTemplateStatusResult> GetStatusAsync(ChannelIntegration integration, WhatsAppTemplate template, CancellationToken ct = default);

    /// <summary>Sends the template's current category and content to Meta as an edit of the one it already has (rejected
    /// or paused), for review again. Throws WhatsAppTemplateProviderException when Meta refuses.</summary>
    Task EditAsync(ChannelIntegration integration, WhatsAppTemplate template, CancellationToken ct = default);

    /// <summary>Deletes the template at Meta. Throws WhatsAppTemplateProviderException when Meta refuses.</summary>
    Task DeleteAsync(ChannelIntegration integration, WhatsAppTemplate template, CancellationToken ct = default);
}
