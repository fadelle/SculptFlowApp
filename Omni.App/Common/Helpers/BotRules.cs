using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Dtos.Knowledge;
using Omni.App.Entities.Models;

namespace Omni.App.Common.Helpers;

/// <summary>The rules every bot follows, shared by the Bots pages, Inbox settings and the AI runtime.</summary>
public static class BotRules
{
    /// <summary>The actions the bot really runs: a template's own, or a custom bot's known choices in catalog order.</summary>
    public static IReadOnlyList<string> Capabilities(Bot bot) =>
        BotTemplates.For(bot.Template)?.Capabilities
        ?? BotCapability.All.Where(c => bot.Capabilities.Contains(c, StringComparer.Ordinal)).ToList();

    /// <summary>The instructions the bot runs: a template's locked core ones, or a custom bot's own (null when it has none).</summary>
    public static string? Instructions(Bot bot) =>
        BotTemplates.For(bot.Template)?.CoreInstructions ?? (string.IsNullOrWhiteSpace(bot.Instructions) ? null : bot.Instructions);

    /// <summary>The active bots that may answer under a plan allowing <paramref name="limit"/> active bots (null = unlimited):
    /// the oldest ones first, so a downgrade silences the newest.</summary>
    public static IReadOnlySet<Guid> AnsweringBotIds(IEnumerable<Bot> bots, int? limit)
    {
        var active = bots.Where(b => b.Status == BotStatus.Active).OrderBy(b => b.CreatedAt).ThenBy(b => b.Id);
        return (limit is int max ? active.Take(Math.Max(max, 0)) : active).Select(b => b.Id).ToHashSet();
    }

    /// <summary>The bot's search_knowledge limit: null for every source, else its selected documents and website sources.</summary>
    public static KnowledgeSourceFilter? KnowledgeFilter(Bot bot) => bot.KnowledgeScope != BotKnowledgeScope.Selected
        ? null
        : new KnowledgeSourceFilter(
            bot.KnowledgeSources.Where(s => s.KnowledgeDocumentId is not null).Select(s => s.KnowledgeDocumentId!.Value).ToList(),
            bot.KnowledgeSources.Where(s => s.WebsiteSourceId is not null).Select(s => s.WebsiteSourceId!.Value).ToList());

    /// <summary>What still stops the bot from running (activation or a test), as a text key; null when it is ready.</summary>
    public static string? NotReadyKey(Bot bot) => NotReadyKey(Instructions(bot) is not null, bot.KnowledgeScope, bot.KnowledgeSources.Count);

    /// <summary>The same for a bot as the pages show it.</summary>
    public static string? NotReadyKey(BotDto bot) =>
        NotReadyKey(bot.IsTemplate || !string.IsNullOrWhiteSpace(bot.Instructions), bot.KnowledgeScope, bot.KnowledgeDocumentIds.Count + bot.WebsiteSourceIds.Count);

    private static string? NotReadyKey(bool hasInstructions, string knowledgeScope, int sourceCount) =>
        !hasInstructions ? "bots.errors.instructionsRequired"
        : knowledgeScope == BotKnowledgeScope.Selected && sourceCount == 0 ? "bots.errors.sourcesRequired"
        : null;
}
