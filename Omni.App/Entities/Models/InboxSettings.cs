namespace Omni.App.Entities.Models;

/// <summary>How the Inbox handles conversations on one connected account (crm.inbox_settings, one row per channel
/// connection): whether an AI bot answers there, which one, and for which conversations. An account without a row has AI
/// off, so its conversations go to the team. Read and changed through Business/Services/Inbox/InboxSettingsService.</summary>
public class InboxSettings
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Guid ChannelIntegrationId { get; set; }

    /// <summary>AI answers on this account (with BotId set and active).</summary>
    public bool AiEnabled { get; set; }

    /// <summary>The bot that answers on this account; null = none (the team answers).</summary>
    public Guid? BotId { get; set; }

    /// <summary>The bot answers a brand-new conversation (else it starts in human mode).</summary>
    public bool AiBotNewConversations { get; set; } = true;

    /// <summary>The bot may keep answering a conversation a contact reopens (else it moves to human mode).</summary>
    public bool AiBotReopenedConversations { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
