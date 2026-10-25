using Omni.App.Entities.Responses.Inbox;

namespace Omni.App.Entities.Dtos.Inbox;

/// <summary>A hand-over to the team: the conversation after it, and whether this call made the real ai -> human change (false
/// when it was already with the team).</summary>
public record HandoffResult(ConversationResponse Conversation, bool HandedOff);
