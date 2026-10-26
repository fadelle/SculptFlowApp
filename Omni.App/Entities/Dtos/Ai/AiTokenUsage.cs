namespace Omni.App.Entities.Dtos.Ai;

/// <summary>Tokens one model call used (OpenAI <c>usage</c>); zero when the answer had none.</summary>
public sealed record AiTokenUsage(int InputTokens, int OutputTokens, int TotalTokens, int CachedTokens = 0, int ReasoningTokens = 0)
{
    public static readonly AiTokenUsage None = new(0, 0, 0);
}
