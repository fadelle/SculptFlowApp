using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Requests.Inbox;

namespace Omni.App.Business.Contracts.Services.Inbox;

/// <summary>A clinic's Inbox settings (crm.inbox_settings, clinic-wide row) together with what its plan allows. Today: whether
/// the AI bot or staff answers new and reopened conversations. When the bot doesn't answer, the conversation is in human
/// mode (same state as a handoff), so n8n is never called and nothing AI is charged.</summary>
public interface IInboxSettingsService
{
    /// <summary>The saved settings, or the defaults (AI bot on) when the clinic has no row. Throws KeyNotFoundException
    /// when the clinic doesn't exist.</summary>
    Task<InboxSettingsDto> GetAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>The mode a brand-new conversation starts in: ConversationMode.Ai or ConversationMode.Human.</summary>
    Task<string> InitialModeAsync(Guid clinicId, CancellationToken ct = default);

    /// <summary>Saves the clinic-wide row, creating it on first save (also when the plan lacks the AI bot, so the choice
    /// applies once it has it). Throws KeyNotFoundException when the clinic doesn't exist.</summary>
    Task UpdateAsync(Guid clinicId, UpdateInboxSettingsRequest request, CancellationToken ct = default);
}
