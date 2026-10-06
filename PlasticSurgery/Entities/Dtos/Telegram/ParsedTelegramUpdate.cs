using System.Text.Json;
using System.Text.RegularExpressions;
using PlasticSurgery.Common.Enums;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Entities.Dtos.Telegram;

/// <summary>A Telegram Update reduced to what SculptFlow cares about. Nothing downstream of the parser
/// ever sees Telegram's JSON.</summary>
public record ParsedTelegramUpdate(TelegramUpdateKind Kind, long UpdateId, ParsedTelegramMessage? Message, string? IgnoredReason);
