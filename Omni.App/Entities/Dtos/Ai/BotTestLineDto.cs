namespace Omni.App.Entities.Dtos.Ai;

/// <summary>One line of a bot test conversation: Role is a BotTestRole; Text is the message, or a note's words in the
/// reader's language.</summary>
public record BotTestLineDto(Guid Id, string Role, string Text, DateTimeOffset CreatedAt);
