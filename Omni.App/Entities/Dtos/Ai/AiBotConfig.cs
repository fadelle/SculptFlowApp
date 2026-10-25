namespace Omni.App.Entities.Dtos.Ai;

/// <summary>The bot an AI trigger runs (AiTriggerPayload.Bot): n8n puts Instructions (BotInstructions: the bot's own
/// instructions, allowed actions, handoff policy and reply language) into its generic system prompt and sends Id back as the
/// X-Omni-Bot-Id header on every tool call, so Omni checks the bot's actions itself. Capabilities are the BotCapability values
/// the bot may use (booking ones only while the plan includes bookings); Language is a fixed reply language (null = the
/// contact's).</summary>
public record AiBotConfig(
    Guid Id,
    string Name,
    string Template,
    string Instructions,
    IReadOnlyList<string> Capabilities,
    string HandoffPolicy,
    string? Language);
