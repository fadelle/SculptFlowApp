using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Business.Contracts.Engines.Ai;

/// <summary>
/// Runs the AI bot for one contact message (or bot test message) the way the n8n AI assistant workflow did: the agent
/// (system prompt, memory, the bot's tools, a JSON decision), then reply / schedule → formatter → reply / cancel →
/// formatter → reply / handoff. Every step is traced; nothing is thrown for a bot or model failure (it is traced and the
/// run ends with nothing sent, like a failed n8n execution).
/// </summary>
public interface IBotEngine
{
    /// <summary><paramref name="trigger"/> is complete (BotTrigger added the bot and the client's profile).</summary>
    Task RunAsync(AiTriggerPayload trigger, CancellationToken ct = default);
}
