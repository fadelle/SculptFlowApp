namespace Omni.App.Entities.Dtos.Ai;

/// <summary>
/// The model's answer to one call. With <see cref="FunctionCalls"/> the model wants tools run: append
/// <see cref="OutputItems"/> (the calls, and any reasoning items, exactly as they came) and one
/// AiInputItem.FunctionOutput per call to the input, then call again. Without them <see cref="OutputText"/> is the final
/// text (JSON when a schema was asked for). <see cref="Status"/> is OpenAI's (completed, incomplete, ...).
/// </summary>
public sealed record AiModelResponse(
    string Id,
    string Status,
    string? OutputText,
    IReadOnlyList<AiFunctionCall> FunctionCalls,
    IReadOnlyList<AiInputItem> OutputItems,
    AiTokenUsage Usage,
    double DurationMs)
{
    public bool WantsTools => FunctionCalls.Count > 0;
}
