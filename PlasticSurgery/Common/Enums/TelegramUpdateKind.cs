using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlasticSurgery.Common.Enums;

public enum TelegramUpdateKind
{
    /// <summary>A private-chat message from a person to the bot — the only kind handled today.</summary>
    CustomerMessage,
    /// <summary>Anything else (edited messages, group chats, channel posts, bot senders, ...) —
    /// acknowledged with 200 so Telegram doesn't retry, and otherwise dropped.</summary>
    Ignored
}
