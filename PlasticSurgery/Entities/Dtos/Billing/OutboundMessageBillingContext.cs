using Microsoft.Extensions.Options;
using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Dtos.Billing;

/// <summary>What a channel's billing policy may look at for an outbound message. No body, no provider payload.</summary>
/// <param name="MessageId">Generated before the send and reused for the Message row, so the reservation and the
/// later delivery callback share one idempotency key.</param>
/// <param name="RecipientAddress">Phone number (or other address) — used only to derive the destination country,
/// never stored.</param>
public sealed record OutboundMessageBillingContext(
    Guid ClinicId,
    string Channel,
    Guid MessageId,
    Guid ConversationId,
    Guid? CampaignId,
    OutboundMessageKind Kind,
    string? TemplateCategory,
    bool ServiceWindowOpen,
    string? RecipientAddress);
