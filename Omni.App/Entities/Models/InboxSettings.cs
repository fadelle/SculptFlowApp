namespace Omni.App.Entities.Models;

/// <summary>How a clinic's Inbox handles conversations (crm.inbox_settings), one column per setting. A clinic without a row
/// uses these defaults. ChannelIntegrationId null = the clinic-wide row, the only kind used today; a per-channel row may
/// later override it. Read and changed through Business/Services/Inbox/InboxSettingsService.</summary>
public class InboxSettings
{
    public Guid Id { get; set; }
    public Guid ClinicId { get; set; }
    public Guid? ChannelIntegrationId { get; set; }

    /// <summary>The AI bot answers a brand-new conversation (else it starts in human mode).</summary>
    public bool AiBotNewConversations { get; set; } = true;

    /// <summary>The AI bot may keep answering a conversation a patient reopens (else it moves to human mode).</summary>
    public bool AiBotReopenedConversations { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
