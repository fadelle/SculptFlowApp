namespace Omni.App.Entities.Dtos.Ai;

/// <summary>The Bots page: the client's bots and what its plan allows. ActiveBotLimit is the plan's max_ai_bots (with
/// add-ons; null = unlimited); OverLimitBotIds are active bots beyond it (after a downgrade), which answer nobody.</summary>
public record BotListDto(
    bool PlanIncludesAi,
    int? ActiveBotLimit,
    int ActiveBotCount,
    IReadOnlyList<BotDto> Bots,
    IReadOnlySet<Guid> OverLimitBotIds);
