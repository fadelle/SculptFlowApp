using Omni.App.Common.Enums;

namespace Omni.App.Entities.Models;

/// <summary>One target contact within a Campaign. Tracks per-recipient send/delivery lifecycle without
/// being a message store itself — the actual send produces a row in messages (Message.CampaignRecipientId
/// points back here). Unique per (campaign, contact) — see ux_campaign_recipients_campaign_contact.</summary>
public class CampaignRecipient
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid ContactId { get; set; }
    public Guid? ConversationId { get; set; }

    /// <summary>The outbound Message this recipient's template send produced — see
    /// Message.CampaignRecipientId for the reverse link. Named MessageId (not
    /// OutboundMessageId) — every message a CampaignRecipient references is by definition
    /// outbound, so the qualifier would be redundant.</summary>
    public Guid? MessageId { get; set; }

    /// <summary>Meta's wamid for the outbound send, duplicated from Message.ExternalMessageId so a
    /// delivery/read/failed webhook can look up the recipient directly if needed — the primary path
    /// (MessageService.HandleStatusUpdateAsync via Message.CampaignRecipientId) doesn't need it, but
    /// keeping it here avoids ever having to join through messages for reporting.</summary>
    public string? ExternalMessageId { get; set; }

    /// <summary>Set when this recipient later makes a booking attributed to this campaign.</summary>
    public Guid? BookingId { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;
    /// <summary>Raw JSON — this recipient's values for the template's {{1}}, {{2}}... placeholders.</summary>
    public string? VariablesJson { get; set; }
    public string Status { get; set; } = CampaignRecipientStatus.Pending;

    /// <summary>Why this contact was excluded from sending despite matching the campaign's audience
    /// (e.g. "no_phone", "opted_out") — set instead of ever creating the row, or before it's queued.
    /// Distinct from FailureCode/FailureReason, which describe a send that was attempted and failed.</summary>
    public string? SkipReason { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureReason { get; set; }

    public DateTimeOffset? QueuedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    /// <summary>First inbound whatsapp_customer message in this recipient's conversation after
    /// SentAt — set by whatever computes campaign reply attribution (see
    /// CampaignService.ComputeStats for the current best-effort read-time version; this column lets
    /// that become a persisted one-time write later without a schema change).</summary>
    public DateTimeOffset? RepliedAt { get; set; }
    /// <summary>Set together with BookingId when a booking is attributed to this campaign.</summary>
    public DateTimeOffset? BookedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Campaign? Campaign { get; set; }
    public Contact? Contact { get; set; }
    public Booking? Booking { get; set; }
}
