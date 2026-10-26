namespace Omni.App.Entities.Dtos.Ai;

/// <summary>Who makes an AI tool call (IBotToolService.CallAsync): the client, the bot and, in a bot test conversation, the
/// test session, as the X-Omni-Bot-Id / X-Omni-Test-Session headers name them on /api/ai. ConversationId and ContactId only
/// label the bot.tool_called trace step.</summary>
public record BotToolCall(Guid ClientId, Guid? BotId, Guid? TestSessionId, Guid? ConversationId = null, Guid? ContactId = null);
