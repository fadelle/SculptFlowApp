using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Business.Contracts.HttpClients.OpenAi;

/// <summary>
/// The AI model behind the bot, the result formatter and the benchmark question generator: one call of the OpenAI
/// Responses API (OpenAiResponsesClient). The caller runs the function-call loop (AiModelResponse.WantsTools). Tests use a
/// fake. Throws AiModelException for every failure (not configured, unreachable, timeout after Ai / TimeoutSeconds, an
/// error answer, an unreadable answer).
/// </summary>
public interface IAiModelClient
{
    Task<AiModelResponse> CreateResponseAsync(AiModelRequest request, CancellationToken ct = default);
}
