using Microsoft.Extensions.Localization;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Ui;

namespace Omni.App.Common.Helpers;

/// <summary>
/// Billing words in the reader's language: plan features and limits (<c>billing.feature.{key}</c>), usage types
/// (<c>billing.eventType.{type}</c>), ledger entries (<c>billing.ledger.{type}</c>) and who pays a channel's usage
/// (<c>billing.paidBy.*</c>). Keys are built from the stored value in camelCase, like <see cref="FilterLabelHelper"/>.
/// The billing services keep their English labels for the admin portal; the client's Billing page reads these.
/// </summary>
public static class BillingTextHelper
{
    public const string EntitlementKeyPrefix = "billing.feature.";
    public const string EventTypeKeyPrefix = "billing.eventType.";
    public const string LedgerKeyPrefix = "billing.ledger.";

    /// <summary>A plan feature or limit as an argument of a server message (e.g. "Campaigns isn't included in your plan").</summary>
    public static TextArg EntitlementArg(string entitlementKey) => TextArg.Of(EntitlementKeyPrefix + FilterLabelHelper.KeyPart(entitlementKey));

    public static string EntitlementLabel(this IStringLocalizer text, string entitlementKey, string fallback) =>
        Find(text, EntitlementKeyPrefix, entitlementKey) ?? fallback;

    /// <summary>A usage type's label; a type with no key falls back to BillableEventTypes' readable form.</summary>
    public static string EventTypeLabel(this IStringLocalizer text, string eventType) =>
        Find(text, EventTypeKeyPrefix, eventType) ?? BillableEventTypes.Label(eventType);

    public static string LedgerLabel(this IStringLocalizer text, string entryType) =>
        Find(text, LedgerKeyPrefix, entryType) ?? FilterLabelHelper.Readable(entryType);

    /// <summary>Who pays for a channel's usage, in client-facing words. Never names Omni's own provider (white-label);
    /// the client's own Meta account may be named because it's theirs.</summary>
    public static string PaidByLabel(this IStringLocalizer text, string providerBilling, string channel, bool charged)
    {
        var label = providerBilling switch
        {
            ProviderBillingResponsibility.PlatformFunded => charged ? text["billing.paidBy.platformCharged"].Value : text["billing.paidBy.platformCovered"].Value,
            ProviderBillingResponsibility.CustomerDirect => channel == ChannelType.WhatsApp ? text["billing.paidBy.youToMeta"].Value : text["billing.paidBy.youToProvider"].Value,
            ProviderBillingResponsibility.ExternalProviderDirect => text["billing.paidBy.yourProvider"].Value,
            ProviderBillingResponsibility.NoProviderUsageFee => text["billing.paidBy.noFee"].Value,
            _ => FilterLabelHelper.Readable(providerBilling)
        };
        return charged && providerBilling != ProviderBillingResponsibility.PlatformFunded ? text["billing.paidBy.plusFee", label].Value : label;
    }

    private static string? Find(IStringLocalizer text, string prefix, string value)
    {
        if (FilterLabelHelper.KeyPart(value) is not { } part) return null;
        var label = text[prefix + part];
        return label.ResourceNotFound ? null : label.Value;
    }
}
