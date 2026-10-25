using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Models;

namespace Omni.App.Business.Mappers.Ai;

public static class BotMapper
{
    public static BotDto ToDto(Bot bot, int accountCount) => new(
        bot.Id, bot.Name, bot.Description, bot.Template,
        bot.Instructions, BotRules.Capabilities(bot), bot.KnowledgeScope,
        bot.KnowledgeSources.Where(s => s.KnowledgeDocumentId is not null).Select(s => s.KnowledgeDocumentId!.Value).ToList(),
        bot.KnowledgeSources.Where(s => s.WebsiteSourceId is not null).Select(s => s.WebsiteSourceId!.Value).ToList(),
        bot.Language, bot.HandoffPolicy, bot.HandoffMessage, bot.Status, accountCount, bot.CreatedAt, bot.UpdatedAt);
}
