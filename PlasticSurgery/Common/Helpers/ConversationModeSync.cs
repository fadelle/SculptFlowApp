using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Common.Helpers;

/// <summary>Single place that changes Conversation.Mode — keeps the legacy AiEnabled/HumanTakeover
/// booleans in sync so nothing writes them independently and lets them drift. Used by both
/// ConversationService (explicit Take Over/Return to AI/Close) and MessageService (implicit mode
/// changes triggered by a staff message arriving from the dashboard or the WhatsApp Business app).</summary>
public static class ConversationModeSync
{
    public static void Apply(Conversation conversation, string mode, DateTimeOffset? at = null)
    {
        var previous = conversation.Mode;
        conversation.Mode = mode;
        conversation.AiEnabled = mode == ConversationMode.Ai;
        conversation.HumanTakeover = mode == ConversationMode.Human;

        var note = previous == ConversationMode.Ai && mode == ConversationMode.Human ? "Handed from AI to staff"
            : previous == ConversationMode.Human && mode == ConversationMode.Ai ? "Returned to AI"
            : null;
        if (note is null) return;

        // A timeline marker the Inbox renders as a divider. Id left unset so EF inserts it (a preset key on a
        // navigation-discovered entity would be treated as an update); dated just before the triggering message
        // (`at`) so it sorts above it.
        conversation.Messages.Add(new Message
        {
            ClinicId = conversation.ClinicId,
            ConversationId = conversation.Id,
            LeadId = conversation.LeadId,
            Direction = MessageDirection.Outbound,
            SenderType = MessageSenderType.System,
            Channel = conversation.Channel,
            MessageType = "text",
            Content = note,
            Origin = MessageOrigin.System,
            CreatedAt = (at ?? DateTimeOffset.UtcNow).AddMilliseconds(-1)
        });
    }
}
