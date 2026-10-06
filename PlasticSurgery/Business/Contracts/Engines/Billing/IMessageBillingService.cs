using Microsoft.Extensions.Options;
using PlasticSurgery.Entities.Dtos.Billing;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Contracts.Engines.Billing;

/// <summary>
/// The bridge between messaging and billing, used by MessageService. The channel policy says WHAT the usage is;
/// IProviderBillingService says, for the clinic's connected account, WHO pays the provider and WHETHER SculptFlow
/// charges. Only charged usage reserves money; everything else is just recorded.
///   ReserveOutboundAsync  before the provider call (throws BillingDeniedException when charged usage can't be paid)
///   SendFailedAsync       the provider call threw -> release
///   SentAsync             the provider accepted -> settle now if the channel bills on acceptance
///   DeliveryStatusChangedAsync  a status callback (any provider) -> the channel policy says settle / release
///   ResolveStaleReservationsAsync  worker: reservations whose outcome never arrived
/// Does nothing while Billing:Enabled is false.
/// </summary>
public interface IMessageBillingService
{
    Task<MessageBillingHold?> ReserveOutboundAsync(OutboundMessageBillingContext message, CancellationToken ct = default);
    Task SendFailedAsync(MessageBillingHold? hold, CancellationToken ct = default);
    Task SentAsync(MessageBillingHold? hold, CancellationToken ct = default);
    Task DeliveryStatusChangedAsync(Message message, string? status, CancellationToken ct = default);
    Task<int> ResolveStaleReservationsAsync(CancellationToken ct = default);
}
