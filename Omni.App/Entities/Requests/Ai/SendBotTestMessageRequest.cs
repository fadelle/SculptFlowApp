namespace Omni.App.Entities.Requests.Ai;

/// <summary>A message to a bot in its test conversation (POST /api/bots/{botId}/test/messages); SessionId null starts one.</summary>
public record SendBotTestMessageRequest(Guid? SessionId, string? Text);
