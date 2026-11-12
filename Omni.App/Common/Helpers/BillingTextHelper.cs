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
    public const string PlanNameKeyPrefix = "billing.planName.";

    /// <summary>A plan's name in the reader's language: billing.planName.{code in camelCase} for the seeded plans ("Free Trial"
    /// was English in ar/fr; QA t2-billing-16), else the plan's own name.</summary>
    public static string PlanName(this IStringLocalizer text, string? planCode, string? planName) =>
        (planCode is null ? null : Find(text, PlanNameKeyPrefix, planCode)) ?? planName ?? string.Empty;

    /// <summary>An amount in the reader's number and currency format ("$25.50", "25,50 $", QA t2-billing-16: French read
    /// "$25,50"): USD is "$", another currency its code.</summary>
    public static string Money(decimal value, string? currency)
    {
        var format = (System.Globalization.NumberFormatInfo)TextCulture.Current.NumberFormat.Clone();
        format.CurrencySymbol = string.IsNullOrWhiteSpace(currency) || currency.Trim() == "USD" ? "$" : currency.Trim();
        return value.ToString("C2", format);
    }

    /// <summary>A plan feature or limit as an argument of a server message (e.g. "Campaigns isn't included in your plan").</summary>
    private const string OverUsersKey = "billing.quota.over.users";
    private const string OverChannelsKey = "billing.quota.over.channels";
    private const string OverWhatsAppKey = "billing.quota.over.whatsapp";
    private const string OverBotsKey = "billing.quota.over.bots";
    private const string OverOtherKey = "billing.quota.over.other";

    /// <summary>The "you're over this limit, here is what to do" text of a plan limit (QA t2-billing-11).</summary>
    public static string OverLimitKey(string entitlementKey) => entitlementKey switch
    {
        EntitlementKeys.MaxUsers => OverUsersKey,
        EntitlementKeys.MaxChannelConnections => OverChannelsKey,
        EntitlementKeys.MaxWhatsAppNumbers => OverWhatsAppKey,
        EntitlementKeys.MaxAiBots => OverBotsKey,
        _ => OverOtherKey
    };

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
