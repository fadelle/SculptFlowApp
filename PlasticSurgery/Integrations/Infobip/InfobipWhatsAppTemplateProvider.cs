using System.Text.Json;
using System.Text.RegularExpressions;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Services;

namespace PlasticSurgery.Integrations.Infobip;

/// <summary>
/// Template review through Infobip (/whatsapp/2/senders/{sender}/templates) for the clinic's sender. WhatsApp
/// (Meta) still does the review; Infobip only relays it. The template id Infobip returns is the WhatsApp
/// template id, stored in WhatsAppTemplate.MetaTemplateId like Meta's.
///
/// Infobip's "structure" differs from Meta's components: body examples are required whenever the body has
/// {{n}} placeholders, buttons use camelCase (phoneNumber), and status vocabularies add FIRST_PAUSED /
/// SECOND_PAUSED / REINSTATED — translated back to WhatsApp's own words here so WhatsAppTemplateService maps
/// both providers the same way. Errors shown to staff never name Infobip (white-label rule).
/// </summary>
public class InfobipWhatsAppTemplateProvider : IWhatsAppTemplateProvider
{
    private static readonly Regex Placeholder = new(@"\{\{\s*(\d+)\s*\}\}", RegexOptions.Compiled);

    private readonly IInfobipClient _client;
    private readonly ILogger<InfobipWhatsAppTemplateProvider> _logger;

    public InfobipWhatsAppTemplateProvider(IInfobipClient client, ILogger<InfobipWhatsAppTemplateProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public string Name => ChannelProvider.Infobip;

    public bool IsReady(ChannelIntegration integration) => !string.IsNullOrEmpty(integration.ProviderSenderId);

    public async Task<WhatsAppTemplateSubmitResult> SubmitAsync(ChannelIntegration integration, WhatsAppTemplate template, CancellationToken ct = default)
    {
        try
        {
            var info = await _client.CreateWhatsAppTemplateAsync(integration.ProviderSenderId!, BuildRequest(template), ct);
            return new WhatsAppTemplateSubmitResult(info.Id, ToWhatsAppStatus(info.Status));
        }
        catch (InfobipApiException ex)
        {
            _logger.LogWarning(ex, "Infobip template submit failed for template {TemplateId} ({StatusCode} {ErrorId}).",
                template.Id, ex.StatusCode, ex.ErrorId);
            throw new WhatsAppTemplateProviderException(NeutralReason(ex));
        }
    }

    public async Task<WhatsAppTemplateStatusResult> GetStatusAsync(ChannelIntegration integration, WhatsAppTemplate template, CancellationToken ct = default)
    {
        try
        {
            var info = await _client.GetWhatsAppTemplateAsync(integration.ProviderSenderId!, template.MetaTemplateId!, ct);
            // Infobip's template GET has no rejection reason; it arrives with the template-update webhook instead.
            return new WhatsAppTemplateStatusResult(ToWhatsAppStatus(info.Status), null);
        }
        catch (InfobipApiException ex)
        {
            _logger.LogWarning(ex, "Infobip template status read failed for template {TemplateId}.", template.Id);
            throw new InvalidOperationException("Couldn't get this template's status right now. Try again in a minute.");
        }
    }

    /// <summary>Infobip status -> WhatsApp's own status word (what WhatsAppTemplateService.MapMetaStatus expects).</summary>
    public static string ToWhatsAppStatus(string? status) => status?.ToUpperInvariant() switch
    {
        "FIRST_PAUSED" or "SECOND_PAUSED" => "PAUSED",
        "REINSTATED" => "APPROVED",
        null or "" or "UNKNOWN" => "PENDING",
        var other => other
    };

    /// <summary>The create-template body from our stored fields. Header media, buttons and body examples are only
    /// added when the template uses them.</summary>
    public static object BuildRequest(WhatsAppTemplate template)
    {
        var structure = new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(template.HeaderType) && template.HeaderType != WhatsAppTemplateHeaderType.None)
        {
            if (template.HeaderType == WhatsAppTemplateHeaderType.Text)
            {
                var header = new Dictionary<string, object> { ["format"] = "TEXT", ["text"] = template.HeaderContent ?? string.Empty };
                if (Placeholder.IsMatch(template.HeaderContent ?? string.Empty))
                {
                    header["example"] = "Sample";
                }
                structure["header"] = header;
            }
            else
            {
                // Media headers need a sample file URL for review — HeaderContent holds it for media headers.
                structure["header"] = new Dictionary<string, object>
                {
                    ["format"] = template.HeaderType!.ToUpperInvariant(),
                    ["example"] = template.HeaderContent ?? string.Empty
                };
            }
        }

        var body = new Dictionary<string, object> { ["text"] = template.Body };
        var examples = BodyExamples(template);
        if (examples.Count > 0)
        {
            body["examples"] = examples;
        }
        structure["body"] = body;

        if (!string.IsNullOrWhiteSpace(template.Footer))
        {
            structure["footer"] = new { text = template.Footer };
        }

        var buttons = Buttons(template.ButtonsJson);
        if (buttons.Count > 0)
        {
            structure["buttons"] = buttons;
        }

        return new
        {
            name = template.Name,
            language = template.Language,
            category = template.Category.ToUpperInvariant(),
            structure
        };
    }

    /// <summary>One sample per {{n}} in the body, taken from VariablesJson when it's a list of strings, otherwise
    /// "Sample 1", "Sample 2"... WhatsApp reviews templates with variables only when samples are given.</summary>
    private static List<string> BodyExamples(WhatsAppTemplate template)
    {
        var count = Placeholder.Matches(template.Body).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max();
        if (count == 0) return new List<string>();

        string[] provided = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(template.VariablesJson))
        {
            try
            {
                provided = JsonSerializer.Deserialize<string[]>(template.VariablesJson) ?? Array.Empty<string>();
            }
            catch (JsonException)
            {
                // Not a plain list of strings — fall back to generated samples.
            }
        }

        return Enumerable.Range(1, count)
            .Select(i => i <= provided.Length && !string.IsNullOrWhiteSpace(provided[i - 1]) ? provided[i - 1] : $"Sample {i}")
            .ToList();
    }

    /// <summary>Our ButtonsJson is Meta's button array; Infobip's differs only in naming (phone_number -> phoneNumber,
    /// URL example as a single string).</summary>
    private static List<object> Buttons(string? buttonsJson)
    {
        var result = new List<object>();
        if (string.IsNullOrWhiteSpace(buttonsJson)) return result;

        using var doc = JsonDocument.Parse(buttonsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

        foreach (var b in doc.RootElement.EnumerateArray())
        {
            var type = Str(b, "type")?.ToUpperInvariant();
            var text = Str(b, "text") ?? string.Empty;
            switch (type)
            {
                case "QUICK_REPLY":
                    result.Add(new { type, text });
                    break;
                case "PHONE_NUMBER":
                    result.Add(new { type, text, phoneNumber = Str(b, "phone_number") ?? Str(b, "phoneNumber") ?? string.Empty });
                    break;
                case "URL":
                    var example = b.TryGetProperty("example", out var ex)
                        ? ex.ValueKind == JsonValueKind.Array && ex.GetArrayLength() > 0 ? ex[0].GetString() : ex.ValueKind == JsonValueKind.String ? ex.GetString() : null
                        : null;
                    result.Add(example is null
                        ? new { type, text, url = Str(b, "url") ?? string.Empty }
                        : (object)new { type, text, url = Str(b, "url") ?? string.Empty, example });
                    break;
            }
        }
        return result;
    }

    /// <summary>What staff see as the rejection reason: Infobip's own validation text (it's about the template,
    /// e.g. a bad name) unless it names Infobip, in which case a generic sentence.</summary>
    private static string NeutralReason(InfobipApiException ex)
    {
        if (ex.IsTimeout || !ex.IsRejected)
        {
            return "WhatsApp couldn't take this template right now. Use Retry submit in a minute.";
        }
        var detail = ex.Detail;
        return string.IsNullOrWhiteSpace(detail) || detail.Contains("infobip", StringComparison.OrdinalIgnoreCase)
            ? "WhatsApp didn't accept this template. Check the name, text and placeholders."
            : "WhatsApp didn't accept this template: " + detail;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
