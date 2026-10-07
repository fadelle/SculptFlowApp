using Microsoft.Extensions.Options;
using PlasticSurgery.Business.Contracts.Engines.Billing;
using PlasticSurgery.Business.Services.Inbox;
using PlasticSurgery.Common.Configs;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Common.Helpers;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Engines.Billing;

/// <summary>
/// WhatsApp's billing rules (both providers): translates a WhatsApp send into a generic billable event, and a
/// provider status into settle / release. The billing core never sees a Meta or Infobip shape.
///   template  -> whatsapp_{category}_message, 1 message, priced by the recipient's country + active provider,
///                settled when "delivered"/"read" arrives, released on "failed"
///   free-form -> not billable by default (Meta doesn't charge replies inside the 24h window)
/// </summary>
public class WhatsAppBillingPolicy : IChannelBillingPolicy
{
    private readonly IConfiguration _configuration;
    private readonly Dictionary<string, string> _categoryEvents;
    private readonly string? _serviceMessageEvent;
    private readonly HashSet<string> _freeInsideWindow;
    private readonly HashSet<string> _billableStatuses;
    private readonly HashSet<string> _nonBillableStatuses;

    public WhatsAppBillingPolicy(IConfiguration configuration, IOptions<WhatsAppBillingOptions> options)
    {
        _configuration = configuration;
        var o = options.Value;
        _categoryEvents = new Dictionary<string, string>(
            o.TemplateCategoryEvents is { Count: > 0 } ? o.TemplateCategoryEvents : WhatsAppBillingOptions.DefaultCategoryEvents,
            StringComparer.OrdinalIgnoreCase);
        _serviceMessageEvent = string.IsNullOrWhiteSpace(o.ServiceMessageEvent) ? null : o.ServiceMessageEvent.Trim().ToLowerInvariant();
        _freeInsideWindow = Set(o.FreeInsideServiceWindow, WhatsAppTemplateCategory.Utility);
        _billableStatuses = Set(o.BillableStatuses, "delivered", "read");
        _nonBillableStatuses = Set(o.NonBillableStatuses, "failed");
    }

    public string Channel => ConversationChannel.WhatsApp;

    public ChannelBillingDecision? DescribeOutbound(OutboundMessageBillingContext message)
    {
        string eventType;
        if (message.Kind == OutboundMessageKind.Text)
        {
            if (_serviceMessageEvent is null) return null;
            eventType = _serviceMessageEvent;
        }
        else
        {
            var category = string.IsNullOrWhiteSpace(message.TemplateCategory) ? "unknown" : message.TemplateCategory.Trim().ToLowerInvariant();
            if (message.ServiceWindowOpen && _freeInsideWindow.Contains(category)) return null;
            eventType = _categoryEvents.TryGetValue(category, out var mapped)
                ? mapped
                : "whatsapp_" + new string(category.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').ToArray()) + "_message";
        }

        return new ChannelBillingDecision(
            EventType: eventType,
            Quantity: 1,
            Unit: "message",
            CountryCode: PhoneCountry.FromPhone(message.RecipientAddress),
            Operator: null,
            Provider: WhatsAppService.ActiveProviderName(_configuration),
            SettleWhen: BillingSettlementTrigger.OnDeliveryStatus);
    }

    public DeliveryBillingAction OnDeliveryStatus(string status) =>
        _billableStatuses.Contains(status) ? DeliveryBillingAction.Settle
        : _nonBillableStatuses.Contains(status) ? DeliveryBillingAction.Release
        : DeliveryBillingAction.None;

    public DeliveryBillingAction ResolveStale(Message? message)
    {
        if (message is null) return DeliveryBillingAction.Release;
        if (message.FailedAt is not null) return DeliveryBillingAction.Release;
        var delivered = (message.DeliveredAt is not null && _billableStatuses.Contains("delivered"))
                        || (message.ReadAt is not null && _billableStatuses.Contains("read"))
                        || (message.DeliveryStatus is not null && _billableStatuses.Contains(message.DeliveryStatus.ToLowerInvariant()));
        return delivered ? DeliveryBillingAction.Settle : DeliveryBillingAction.Release;
    }

    private static HashSet<string> Set(List<string>? configured, params string[] defaults) =>
        new((configured is { Count: > 0 } ? configured : defaults.ToList()).Select(s => s.Trim().ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
}
