namespace Omni.App.Entities.Requests.Ai;

/// <summary>Body of the bot's reply (POST /api/ai/conversations/{id}/reply): the text for the contact.</summary>
/// <param name="ReplyId">Optional id of this reply (e.g. n8n's execution id and node): a retry with the same id is the same reply
/// and is neither charged nor sent again (QA t2-billing-02). Without it, the same content after the same customer message is.</param>
public record AiReplyRequest(string? Content, string? ReplyId = null);
