namespace Omni.App.Entities.Dtos.Ai;

/// <summary>One call to the model (IAiModelClient): the model, the system instructions, the input items (memory, the
/// contact's message, the function-call loop so far), the tools it may call and an optional structured output format.</summary>
public sealed record AiModelRequest(
    string Model,
    string? Instructions,
    IReadOnlyList<AiInputItem> Input,
    IReadOnlyList<AiToolSpec>? Tools = null,
    AiJsonSchema? Output = null,
    double? Temperature = null);
