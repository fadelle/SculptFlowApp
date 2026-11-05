using Omni.App.Common.Enums;
using Omni.App.Entities.Models;

using Omni.App.Common.Statics;

namespace Omni.App.Common.Helpers;

/// <summary>Single place that changes Conversation.Mode — keeps the legacy AiEnabled/HumanTakeover
/// booleans in sync so nothing writes them independently and lets them drift. Used by both
/// ConversationService (explicit Take Over/Return to AI/Close) and MessageService (implicit mode
/// changes triggered by a member message arriving from the dashboard or the WhatsApp Business app).</summary>
public static class ConversationModeSync
{
    public static void Apply(Conversation conversation, string mode, DateTimeOffset? at = null)
    {
        var previous = conversation.Mode;
        conversation.Mode = mode;
        conversation.AiEnabled = mode == ConversationMode.Ai;
        conversation.HumanTakeover = mode == ConversationMode.Human;

        // The marker's key (MessageMetadata) is what the Inbox shows, in the reader's language (QA t7-07); the English sentence
        // stays as the content for anything that reads the text alone (an export).
        (string? note, string? marker) = previous == ConversationMode.Ai && mode == ConversationMode.Human ? ("Handed from AI to your team", MessageMetadata.HandedToTeamMarker) // text-ok: fallback, the Inbox words it from the key
            : previous == ConversationMode.Human && mode == ConversationMode.Ai ? ("Returned to AI", MessageMetadata.ReturnedToAiMarker) // text-ok: fallback, the Inbox words it from the key
            : ((string?)null, (string?)null);
        if (note is null) return;

        // A timeline marker the Inbox renders as a divider. Id left unset so EF inserts it (a preset key on a
        // navigation-discovered entity would be treated as an update); dated just before the triggering message
        // (`at`) so it sorts above it.
        conversation.Messages.Add(new Message
        {
            ClientId = conversation.ClientId,
            ConversationId = conversation.Id,
            ContactId = conversation.ContactId,
            Direction = MessageDirection.Outbound,
            SenderType = MessageSenderType.System,
            Channel = conversation.Channel,
            MessageType = "text",
            Content = note,
            MetadataJson = marker,
            Origin = MessageOrigin.System,
            CreatedAt = (at ?? DateTimeOffset.UtcNow).AddMilliseconds(-1)
        });
    }
}
