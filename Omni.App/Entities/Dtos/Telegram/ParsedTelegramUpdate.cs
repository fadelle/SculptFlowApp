using System.Text.Json;
using System.Text.RegularExpressions;
using Omni.App.Common.Enums;
using Omni.App.Entities.Models;

namespace Omni.App.Entities.Dtos.Telegram;

/// <summary>A Telegram Update reduced to what Omni cares about. Nothing downstream of the parser
/// ever sees Telegram's JSON.</summary>
public record ParsedTelegramUpdate(TelegramUpdateKind Kind, long UpdateId, ParsedTelegramMessage? Message, string? IgnoredReason);
