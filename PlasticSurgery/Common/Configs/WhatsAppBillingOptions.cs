using Microsoft.Extensions.Options;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Statics;

namespace PlasticSurgery.Common.Configs;

/// <summary>"Billing:WhatsApp" config. Every WhatsApp pricing assumption lives here (not in the billing core and
/// not in code paths), so a pricing change at Meta/the provider is a config change. Defaults follow per-message
/// pricing (2025): templates are charged per delivered message by category, free-form replies inside the 24h
/// window are free, and utility templates are free while that window is open.</summary>
public class WhatsAppBillingOptions
{
    /// <summary>Template category (lowercase) -> billable event type. A category not listed becomes
    /// "whatsapp_{category}_message", which is then refused until a rate card prices it.</summary>
    public Dictionary<string, string>? TemplateCategoryEvents { get; set; }

    /// <summary>Event type for free-form (non-template) messages. Empty = not billable.</summary>
    public string? ServiceMessageEvent { get; set; }

    /// <summary>Template categories that cost nothing while the customer service window is open.</summary>
    public List<string>? FreeInsideServiceWindow { get; set; }

    /// <summary>Delivery statuses that make a reserved message billable.</summary>
    public List<string>? BillableStatuses { get; set; }

    /// <summary>Delivery statuses that mean it will never be billable (the hold is released).</summary>
    public List<string>? NonBillableStatuses { get; set; }

    internal static readonly Dictionary<string, string> DefaultCategoryEvents = new()
    {
        [WhatsAppTemplateCategory.Marketing] = BillableEventTypes.WhatsAppMarketingMessage,
        [WhatsAppTemplateCategory.Utility] = BillableEventTypes.WhatsAppUtilityMessage,
        [WhatsAppTemplateCategory.Authentication] = BillableEventTypes.WhatsAppAuthenticationMessage,
    };
}
