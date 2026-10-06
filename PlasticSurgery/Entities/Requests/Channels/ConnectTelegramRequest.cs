namespace PlasticSurgery.Entities.Requests.Channels;

/// <summary>Body for POST /api/channel-integrations/telegram/connect. The token is write-only: it is
/// validated with Telegram, stored server-side, and never returned or logged.</summary>
public record ConnectTelegramRequest(string? BotToken);
