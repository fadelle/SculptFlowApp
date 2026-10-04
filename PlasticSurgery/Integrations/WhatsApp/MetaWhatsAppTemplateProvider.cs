using System.Text.Json;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Services;

namespace PlasticSurgery.Integrations.WhatsApp;

/// <summary>Template review directly with Meta (Graph /{waba-id}/message_templates) using the clinic's own
/// Embedded Signup token. Moved here unchanged from WhatsAppTemplateService when the provider seam was added.</summary>
public class MetaWhatsAppTemplateProvider : IWhatsAppTemplateProvider
{
    private readonly IMetaGraphClient _meta;

    public MetaWhatsAppTemplateProvider(IMetaGraphClient meta)
    {
        _meta = meta;
    }

    public string Name => ChannelProvider.Meta;

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
            throw new WhatsAppTemplateProviderException(ex.Message);
        }
    }

    public async Task<WhatsAppTemplateStatusResult> GetStatusAsync(ChannelIntegration integration, WhatsAppTemplate template, CancellationToken ct = default)
    {
        var result = await _meta.GetMessageTemplateStatusAsync(template.MetaTemplateId!, integration.AccessToken!, ct);
        return new WhatsAppTemplateStatusResult(result.Status, result.RejectedReason);
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
