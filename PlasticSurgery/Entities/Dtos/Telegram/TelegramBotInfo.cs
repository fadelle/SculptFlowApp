using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlasticSurgery.Entities.Dtos.Telegram;

/// <summary>Identity of a bot as reported by Telegram's getMe.</summary>
public record TelegramBotInfo(long Id, string? Username, string? FirstName);
