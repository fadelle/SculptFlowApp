using PlasticSurgery.Common.Enums;

namespace PlasticSurgery.Entities.Models;

public class Conversation
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid LeadId { get; set; }

    public string Channel { get; set; } = string.Empty;
    public string? ExternalThreadId { get; set; }
    public string Status { get; set; } = ConversationStatus.Active;

    /// <summary>The real source of truth for who's allowed to reply — see <see cref="ConversationMode"/>.
    /// AiEnabled/HumanTakeover are kept below for backward compatibility with any existing reads,
    /// but every write path in this app now sets Mode and keeps those two flags in sync from it
    /// (see ConversationService) rather than the other way around.</summary>
    public string Mode { get; set; } = ConversationMode.Ai;
    public bool AiEnabled { get; set; } = true;
    public bool HumanTakeover { get; set; } = false;
    public DateTimeOffset? LastMessageAt { get; set; }
    public string? LastMessageDirection { get; set; }

    /// <summary>When the customer last sent a genuine inbound WhatsApp message (Origin ==
    /// WhatsAppCustomer). Only ever set by that one case — see MessageService.IngestAsync's
    /// handling of IngestEventType.CustomerMessage. Staff/AI/campaign sends never touch this.</summary>
    public DateTimeOffset? LastCustomerMessageAt { get; set; }

    /// <summary>Denormalized LastCustomerMessageAt + WhatsApp's 24-hour customer service window,
    /// stored so "is free-form messaging currently allowed" is a plain comparison rather than
    /// interval math everywhere it's checked. Recomputed alongside LastCustomerMessageAt.</summary>
    public DateTimeOffset? ServiceWindowExpiresAt { get; set; }

    /// <summary>When staff last opened this conversation (shared by all the clinic's staff). Inbound
    /// messages newer than this are "unread"; null = never opened, so every inbound message is unread.</summary>
    public DateTimeOffset? LastReadAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Lead? Lead { get; set; }
    public ICollection<Message> Messages { get; set; } = new List<Message>();

    /// <summary>Whether a normal free-form WhatsApp message can be sent right now. False for
    /// non-WhatsApp channels too (the window concept is WhatsApp-specific) — callers should check
    /// Channel separately if they need a WhatsApp-specific reason.</summary>
    public bool IsServiceWindowOpen(DateTimeOffset now) =>
        Channel == ConversationChannel.WhatsApp && ServiceWindowExpiresAt.HasValue && ServiceWindowExpiresAt.Value > now;
}
