namespace Omni.App.Entities.Dtos.Ai;

/// <summary>The answer of one AI tool call (IBotToolService): the HTTP status the /api/ai endpoint answers with and the JSON
/// body the AI reads. Business outcomes (refusals, CONFIRMATION_REQUIRED, ...) are 200 with success = false; 400, 404, 409
/// and 503 are the endpoint's error answers, a null body = an empty one.</summary>
public record BotToolResult(int Status, object? Body)
{
    public static BotToolResult Ok(object? body) => new(200, body);

    public static BotToolResult BadRequest(object body) => new(400, body);

    public static BotToolResult NotFound(object? body = null) => new(404, body);

    public static BotToolResult Conflict(object body) => new(409, body);

    public static BotToolResult Unavailable(object body) => new(503, body);

    public bool IsError => Status >= 400;
}
