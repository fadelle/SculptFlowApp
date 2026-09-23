namespace PlasticSurgery.Data.Entities;

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

public static class ConversationChannel
{
    public const string WhatsApp = "whatsapp";
    public const string Instagram = "instagram";
    public const string Website = "website";
    public const string Facebook = "facebook";
    public const string Sms = "sms";
    public const string Email = "email";
    public const string Telegram = "telegram";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        WhatsApp, Instagram, Website, Facebook, Sms, Email, Telegram
    };
}

public static class ConversationStatus
{
    public const string Active = "active";
    public const string Closed = "closed";
    public const string Archived = "archived";
}

/// <summary>Allowed values for Conversation.Mode — must match the CHECK constraint in Database/schema.sql.</summary>
public static class ConversationMode
{
    /// <summary>AI/n8n is allowed to automatically reply.</summary>
    public const string Ai = "ai";
    /// <summary>Messages are saved and shown, but AI must not automatically reply — staff is handling it.</summary>
    public const string Human = "human";
    /// <summary>AI may draft a suggested reply, but staff approval is required before it's sent.</summary>
    public const string Approval = "approval";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Ai, Human, Approval };
}

/// <summary>Single place that changes Conversation.Mode — keeps the legacy AiEnabled/HumanTakeover
/// booleans in sync so nothing writes them independently and lets them drift. Used by both
/// ConversationService (explicit Take Over/Return to AI/Close) and MessageService (implicit mode
/// changes triggered by a staff message arriving from the dashboard or the WhatsApp Business app).</summary>
public static class ConversationModeSync
{
    public static void Apply(Conversation conversation, string mode, DateTimeOffset? at = null)
    {
        var previous = conversation.Mode;
        conversation.Mode = mode;
        conversation.AiEnabled = mode == ConversationMode.Ai;
        conversation.HumanTakeover = mode == ConversationMode.Human;

        var note = previous == ConversationMode.Ai && mode == ConversationMode.Human ? "Handed from AI to staff"
            : previous == ConversationMode.Human && mode == ConversationMode.Ai ? "Returned to AI"
            : null;
        if (note is null) return;

        // A timeline marker the Inbox renders as a divider. Id left unset so EF inserts it (a preset key on a
        // navigation-discovered entity would be treated as an update); dated just before the triggering message
        // (`at`) so it sorts above it.
        conversation.Messages.Add(new Message
        {
            ClinicId = conversation.ClinicId,
            ConversationId = conversation.Id,
            LeadId = conversation.LeadId,
            Direction = MessageDirection.Outbound,
            SenderType = MessageSenderType.System,
            Channel = conversation.Channel,
            MessageType = "text",
            Content = note,
            Origin = MessageOrigin.System,
            CreatedAt = (at ?? DateTimeOffset.UtcNow).AddMilliseconds(-1)
        });
    }
}
