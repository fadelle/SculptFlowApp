namespace Omni.App.Entities.Dtos.Ai;

/// <summary>One AI tool as Omni checks it (BotTools): <see cref="Route"/> is the /api/ai route template, also the tool name
/// of its bot.tool_called trace step; <see cref="Capability"/> the BotCapability it needs (null = every bot);
/// <see cref="Feature"/> the EntitlementKeys feature the client's plan must include (null = none).</summary>
public record BotToolDefinition(string Route, string? Capability, string? Feature = null);
