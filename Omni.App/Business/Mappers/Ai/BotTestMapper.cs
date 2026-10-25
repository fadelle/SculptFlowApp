using System.Text.Json;
using Microsoft.Extensions.Localization;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Models;

namespace Omni.App.Business.Mappers.Ai;

public static class BotTestMapper
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The transcript in the reader's language: a note (stored as BotTestNote JSON) is worded from its key and
    /// arguments; messages stay as written.</summary>
    public static BotTestTranscriptDto ToTranscript(BotTestSession session, IReadOnlyList<BotTestMessage> lines, IStringLocalizer text)
    {
        var mapped = lines.Select(l => new BotTestLineDto(l.Id, l.Role, l.Role == BotTestRole.Note ? Note(l.Content, text) : l.Content, l.CreatedAt))
            .ToList();
        // The bot owes an answer while the last real exchange is the contact's message (a note after it, e.g. a simulated
        // booking, means the workflow is still on its way to the reply).
        var last = lines.LastOrDefault(l => l.Role is BotTestRole.Contact or BotTestRole.Bot or BotTestRole.Automatic);
        var unreachable = lines.Count > 0 && lines[^1].Role == BotTestRole.Note && lines[^1].Content.Contains(BotTestNote.NotReachableKey, StringComparison.Ordinal);
        var refused = lines.Count > 0 && lines[^1].Role == BotTestRole.Note && lines[^1].Content.Contains(BotTestNote.ReplyRefusedKey, StringComparison.Ordinal);
        var waiting = !session.HandedOff && last?.Role == BotTestRole.Contact && !unreachable && !refused;
        return new BotTestTranscriptDto(session.Id, session.HandedOff, waiting, mapped);
    }

    private static string Note(string content, IStringLocalizer text)
    {
        try
        {
            var note = JsonSerializer.Deserialize<BotTestNote>(content, Json);
            if (note is not null) return text[note.Key, note.Args.Cast<object>().ToArray()].Value;
        }
        catch (JsonException)
        {
            // Not a note JSON: shown as stored.
        }
        return content;
    }
}
