using System.Text.Json;
using Omni.App.Business.Contracts.HttpClients.Meta;
using Omni.App.Business.Contracts.Providers.WhatsApp;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Entities.Dtos.WhatsApp;
using Omni.App.Entities.Models;

namespace Omni.App.Business.Providers.WhatsApp;

/// <summary>Template review directly with Meta (Graph /{waba-id}/message_templates) using the client's own
/// Embedded Signup token.</summary>
public class MetaWhatsAppTemplateProvider : IWhatsAppTemplateProvider
{
    private readonly IMetaGraphClient _meta;

    public MetaWhatsAppTemplateProvider(IMetaGraphClient meta)
    {
        _meta = meta;
    }


    public bool IsReady(ChannelIntegration integration) =>
        !string.IsNullOrEmpty(integration.WhatsAppBusinessId) && !string.IsNullOrEmpty(integration.AccessToken);

    public async Task<WhatsAppTemplateSubmitResult> SubmitAsync(ChannelIntegration integration, WhatsAppTemplate template, CancellationToken ct = default)
    {
        try
        {
            var result = await _meta.CreateMessageTemplateAsync(
                integration.WhatsAppBusinessId!, integration.AccessToken!,
                template.Name, template.Category, template.Language, BuildMetaComponents(template), ct);
            return new WhatsAppTemplateSubmitResult(result.Id, result.Status);
        }
        catch (MetaGraphApiException ex)
        {
            throw WhatsAppTemplateProviderException.From(ex);
        }
        catch (HttpRequestException ex)
        {
            // Meta could not be reached at all (QA t8-12): the template was not judged, it stays a draft.
            throw new WhatsAppTemplateProviderException(ex.Message, couldNotReach: true, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new WhatsAppTemplateProviderException(ex.Message, couldNotReach: true, ex); // timed out
        }
    }

    public async Task<WhatsAppTemplateStatusResult> GetStatusAsync(ChannelIntegration integration, WhatsAppTemplate template, CancellationToken ct = default)
    {
        var result = await _meta.GetMessageTemplateStatusAsync(template.MetaTemplateId!, integration.AccessToken!, ct);
        return new WhatsAppTemplateStatusResult(result.Status, result.RejectedReason);
    }

    public async Task EditAsync(ChannelIntegration integration, WhatsAppTemplate template, CancellationToken ct = default)
    {
        try
        {
            await _meta.EditMessageTemplateAsync(template.MetaTemplateId!, integration.AccessToken!, template.Category, BuildMetaComponents(template), ct);
        }
        catch (MetaGraphApiException ex)
        {
            throw WhatsAppTemplateProviderException.From(ex);
        }
    }

    public async Task DeleteAsync(ChannelIntegration integration, WhatsAppTemplate template, CancellationToken ct = default)
    {
        try
        {
            await _meta.DeleteMessageTemplateAsync(integration.WhatsAppBusinessId!, integration.AccessToken!, template.Name, template.MetaTemplateId!, ct);
        }
        catch (MetaGraphApiException ex)
        {
            throw WhatsAppTemplateProviderException.From(ex);
        }
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
}
