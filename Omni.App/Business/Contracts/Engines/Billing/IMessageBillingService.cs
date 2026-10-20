using Microsoft.Extensions.Options;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Models;

namespace Omni.App.Business.Contracts.Engines.Billing;

/// <summary>
/// The bridge between messaging and billing, used by MessageService. The channel policy says WHAT the usage is;
/// IProviderBillingService says, for the client's connected account, WHO pays the provider and WHETHER Omni
/// charges. Only charged usage reserves money; everything else is just recorded.
///   ReserveOutboundAsync  before the provider call (throws BillingDeniedException when charged usage can't be paid)
///   ReserveAiReplyAsync   before an AI reply is sent: its ai_reply usage (same refusal; settled by SentAsync)
///   SendFailedAsync       the provider call threw -> release
///   SentAsync             the provider accepted -> settle now if the channel bills on acceptance
///   DeliveryStatusChangedAsync  a status callback (any provider) -> the channel policy says settle / release
///   ResolveStaleReservationsAsync  worker: reservations whose outcome never arrived
/// Does nothing while Billing:Enabled is false.
/// </summary>
public interface IMessageBillingService
{
    Task<MessageBillingHold?> ReserveOutboundAsync(OutboundMessageBillingContext message, CancellationToken ct = default);

    /// <summary>Reserves one ai_reply for an AI assistant reply (key ai:reply:{messageId}), on top of the channel's own
    /// message usage. Who pays the AI provider: Billing:ProviderBilling:Defaults:ai, built-in platform_funded (charged).
    /// Null when billing or Billing:ChargeAiReplies is off. Throws BillingDeniedException (the refusal is recorded as a
    /// failed usage row) when there's no rate or the included credit + wallet can't cover it. Settle with SentAsync,
    /// release with SendFailedAsync.</summary>
    Task<MessageBillingHold?> ReserveAiReplyAsync(Guid clientId, Guid conversationId, Guid messageId, CancellationToken ct = default);
    Task SendFailedAsync(MessageBillingHold? hold, CancellationToken ct = default);
    Task SentAsync(MessageBillingHold? hold, CancellationToken ct = default);
    Task DeliveryStatusChangedAsync(Message message, string? status, CancellationToken ct = default);
    Task<int> ResolveStaleReservationsAsync(CancellationToken ct = default);
}
