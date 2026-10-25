namespace Omni.App.Entities.Requests.Ai;

/// <summary>Body of the bot's reply (POST /api/ai/conversations/{id}/reply): the text for the contact.</summary>
public record AiReplyRequest(string? Content);
