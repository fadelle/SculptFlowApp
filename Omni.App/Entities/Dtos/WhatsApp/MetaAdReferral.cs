namespace Omni.App.Entities.Dtos.WhatsApp;

/// <summary>The click-to-WhatsApp ad (or post) a customer message came from: Meta's "referral" object on the message
/// (source_type "ad" or "post", source_id = the ad's id, headline and body = the ad's text, ctwa_clid = the click id Meta's
/// Conversions API needs). QA t10-08.</summary>
public record MetaAdReferral(string? SourceType, string? SourceId, string? SourceUrl, string? Headline, string? Body, string? ClickId)
{
    /// <summary>What the ad offered, in one line for the AI and the contact's source detail: the headline, else the body,
    /// else the ad id.</summary>
    public string? Summary =>
        new[] { Headline, Body }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList() is { Count: > 0 } parts
            ? string.Join(" — ", parts)
            : SourceId is null ? null : $"ad {SourceId}";
}
