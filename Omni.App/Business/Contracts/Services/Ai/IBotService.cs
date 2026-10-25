using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Requests.Ai;

namespace Omni.App.Business.Contracts.Services.Ai;

/// <summary>
/// A client's AI bots (crm.bots): what each one does. Rules: a template bot (support, booking, sales) keeps the template's
/// locked instructions and actions; a custom bot needs its own instructions; only an active bot answers, and the plan's
/// max_ai_bots (with add-ons) caps how many are active at once; activating needs the plan's AI assistant. Where a bot answers
/// is IInboxSettingsService's job. Throws KeyNotFoundException for another client's or an unknown bot, ArgumentException for a
/// form error, InvalidOperationException for a bot that isn't ready and EntitlementDeniedException for a plan limit.
/// </summary>
public interface IBotService
{
    /// <summary>The Bots page: every bot (oldest first) with how many accounts use it, and the plan's limits.</summary>
    Task<BotListDto> ListAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>One bot, or null when the client has no such bot.</summary>
    Task<BotDto?> GetAsync(Guid clientId, Guid botId, CancellationToken ct = default);

    /// <summary>Creates a draft bot from <paramref name="template"/> (BotTemplate) named <paramref name="name"/> (the template's
    /// name in the member's language); a custom one starts without instructions.</summary>
    Task<BotDto> CreateAsync(Guid clientId, string template, string name, CancellationToken ct = default);

    /// <summary>"Use as Custom Bot": an editable draft copy of a bot, with the template's instructions and actions written out
    /// (a custom bot is copied as it is).</summary>
    Task<BotDto> DuplicateAsCustomAsync(Guid clientId, Guid botId, string name, CancellationToken ct = default);

    /// <summary>Saves the editor's form (including the status).</summary>
    Task<BotDto> SaveAsync(Guid clientId, Guid botId, SaveBotRequest request, CancellationToken ct = default);

    /// <summary>Changes only the status (the Bots list's Activate / Deactivate).</summary>
    Task<BotDto> SetStatusAsync(Guid clientId, Guid botId, string status, CancellationToken ct = default);

    /// <summary>Deletes the bot; the accounts that used it keep AI on but have no bot, so the team answers them.</summary>
    Task DeleteAsync(Guid clientId, Guid botId, CancellationToken ct = default);

    /// <summary>The Knowledge Base sources the editor offers (documents first, then website sources).</summary>
    Task<IReadOnlyList<BotKnowledgeChoice>> ListKnowledgeChoicesAsync(Guid clientId, CancellationToken ct = default);
}
