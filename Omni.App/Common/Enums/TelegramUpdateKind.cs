using System.Text.Json;
using System.Text.RegularExpressions;

namespace Omni.App.Common.Enums;

public enum TelegramUpdateKind
{
    /// <summary>A private-chat message from a person to the bot — the only kind handled today.</summary>
    CustomerMessage,
    /// <summary>The person edited a message they sent (edited_message): the stored text is updated (QA t3-inbox-17).</summary>
    EditedMessage,
    /// <summary>The person blocked the bot (my_chat_member, status kicked): the contact is opted out, nothing more is sent.</summary>
    BotBlocked,
    /// <summary>The person unblocked / restarted the bot (my_chat_member, status member): the contact is opted back in.</summary>
    BotUnblocked,
    /// <summary>Anything else (edited messages, group chats, channel posts, bot senders, ...) —
    /// acknowledged with 200 so Telegram doesn't retry, and otherwise dropped.</summary>
    Ignored
}
