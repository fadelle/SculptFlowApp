namespace PlasticSurgery.Data.Entities;

/// <summary>One target lead within a Campaign. Tracks per-recipient send/delivery lifecycle without
/// being a message store itself — the actual send produces a row in messages (Message.CampaignRecipientId
/// points back here). Unique per (campaign, lead) — see ux_campaign_recipients_campaign_lead.</summary>
public class CampaignRecipient
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid LeadId { get; set; }
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

    /// <summary>Set when this recipient later books a consultation attributed to this campaign.</summary>
    public Guid? AppointmentId { get; set; }

    public string PhoneNumber { get; set; } = string.Empty;
    /// <summary>Raw JSON — this recipient's values for the template's {{1}}, {{2}}... placeholders.</summary>
    public string? VariablesJson { get; set; }
    public string Status { get; set; } = CampaignRecipientStatus.Pending;

    /// <summary>Why this lead was excluded from sending despite matching the campaign's audience
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
    /// <summary>Set together with AppointmentId when a booking is attributed to this campaign.</summary>
    public DateTimeOffset? BookedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Campaign? Campaign { get; set; }
    public Lead? Lead { get; set; }
    public Appointment? Appointment { get; set; }
}

public static class CampaignRecipientStatus
{
    /// <summary>Created (part of the audience snapshot) but the campaign hasn't started sending yet.</summary>
    public const string Pending = "pending";
    public const string Queued = "queued";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Read = "read";
    public const string Replied = "replied";
    public const string Booked = "booked";
    public const string Failed = "failed";
    /// <summary>Was part of the audience snapshot but deliberately not sent to — see SkipReason.</summary>
    public const string Skipped = "skipped";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Pending, Queued, Sent, Delivered, Read, Replied, Booked, Failed, Skipped
    };
}
