namespace Omni.App.Entities.Dtos.Inbox;

/// <summary>One channel on the Auto-respond page: its settings, whether the client has it connected, and whether the AI bot
/// answers new / reopened conversations there (the auto-reply then waits for SendWhenAiAnswers).</summary>
public record AutoReplyChannelDto(AutoReplyDto Settings, bool Connected, bool AiBotAnswersNew, bool AiBotAnswersReopened);
