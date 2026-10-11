using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PlasticSurgery.Common.Exceptions;
using PlasticSurgery.Entities.Dtos.Telegram;

namespace PlasticSurgery.Business.Contracts.HttpClients.Telegram;

/// <summary>
/// The one class that talks to the Telegram Bot API — the Telegram counterpart of MetaGraphClient /
/// WhatsAppService. Every method takes the bot token as a parameter; nothing here stores it.
///
/// SECRET HANDLING: Telegram puts the token in the URL path (/bot{token}/method). Two consequences,
/// both handled: (1) this client is registered with RemoveAllLoggers() in Program.cs so the framework's
/// HttpClient logging (which prints request URIs) never sees it; (2) failures are re-thrown as
/// <see cref="TelegramApiException"/> with a message built only from Telegram's error description —
/// never from the request URL.
/// </summary>
public interface ITelegramBotClient
{
    Task<TelegramBotInfo> GetMeAsync(string botToken, CancellationToken ct = default);

    /// <summary>Registers <paramref name="url"/> as the bot's webhook; Telegram will send
    /// <paramref name="secretToken"/> back in X-Telegram-Bot-Api-Secret-Token on every delivery.</summary>
    Task SetWebhookAsync(string botToken, string url, string secretToken, CancellationToken ct = default);

    Task DeleteWebhookAsync(string botToken, CancellationToken ct = default);

    Task<TelegramWebhookInfo> GetWebhookInfoAsync(string botToken, CancellationToken ct = default);

    /// <summary>Sends plain text (no parse_mode, so no markup escaping surprises) and returns the
    /// Telegram message_id of the sent message.</summary>
    Task<long> SendMessageAsync(string botToken, string chatId, string text, CancellationToken ct = default);

    /// <summary>getFile: where Telegram keeps a file a user sent (voice, photo...), from the file_id in the update.</summary>
    Task<TelegramFileInfo> GetFileAsync(string botToken, string fileId, CancellationToken ct = default);

    /// <summary>Downloads a file by the path getFile returned. Throws <see cref="TelegramApiException"/> when the file is
    /// larger than <paramref name="maxBytes"/> or Telegram refuses; the token is never in the message.</summary>
    Task<byte[]> DownloadFileAsync(string botToken, string filePath, int maxBytes, CancellationToken ct = default);
}
