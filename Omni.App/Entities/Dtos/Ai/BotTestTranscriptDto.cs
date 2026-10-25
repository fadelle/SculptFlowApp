namespace Omni.App.Entities.Dtos.Ai;

/// <summary>A bot test conversation as the editor's test panel polls it. Waiting = the bot hasn't answered the last message
/// yet (the workflow runs in the background); HandedOff = the bot handed it to the team, so the test is over until reset.</summary>
public record BotTestTranscriptDto(Guid SessionId, bool HandedOff, bool Waiting, IReadOnlyList<BotTestLineDto> Lines);
