namespace Omni.App.Common.Enums;

/// <summary>Why a campaign recipient wasn't sent (marketing.campaign_recipients.skip_reason, status skipped). Each has a label
/// campaigns.skipReason.{value in camelCase} in the strings files (<see cref="Label"/>).</summary>
public static class CampaignSkipReason
{
    public const string NoPhone = "no_phone";
    public const string OptedOut = "opted_out";
    public const string MarketingOptInFalse = "marketing_opt_in_false";
    /// <summary>The campaign was cancelled before this recipient was sent (QA t5-ops-11: they were marked failed).</summary>
    public const string CampaignCancelled = "campaign_cancelled";

    public static readonly IReadOnlyList<string> All = [NoPhone, OptedOut, MarketingOptInFalse, CampaignCancelled];

    public const string LabelKeyPrefix = "campaigns.skipReason.";

    /// <summary>The translated reason; an unknown stored value is shown readable ("some_reason" → "Some reason").</summary>
    public static string Label(Microsoft.Extensions.Localization.IStringLocalizer text, string reason)
    {
        if (Omni.App.Common.Helpers.FilterLabelHelper.KeyPart(reason) is { } part && text[LabelKeyPrefix + part] is { ResourceNotFound: false } label)
        {
            return label.Value;
        }
        return Omni.App.Common.Helpers.FilterLabelHelper.Readable(reason);
    }
}
