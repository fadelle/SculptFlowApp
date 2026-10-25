namespace Omni.App.Entities.Dtos.Inbox;

/// <summary>The Inbox Settings page: each connected account with its AI settings, the client's bots to pick from, and what
/// its plan allows (PlanIncludesAiBot = the ai_assistant entitlement; ActiveBotLimit = max_ai_bots with add-ons, null =
/// unlimited).</summary>
public record InboxSettingsDto(
    bool PlanIncludesAiBot,
    int? ActiveBotLimit,
    IReadOnlyList<AccountAiSettingsDto> Accounts,
    IReadOnlyList<BotChoiceDto> Bots);
