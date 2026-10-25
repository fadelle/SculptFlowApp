namespace Omni.App.Entities.Dtos.Ai;

/// <summary>A bot reply that went out: the Inbox message's id (null for a test run, whose reply only joins its transcript).</summary>
public record BotReplyOutcome(Guid? MessageId, bool Test);
