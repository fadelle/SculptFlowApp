using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Omni.App.Entities.Responses.Channels;

namespace Omni.App.Business.Contracts.Services.Channels;

/// <summary>
/// Connect / refresh / disconnect a client's Telegram bot. Everything is scoped by the clientId the
/// caller resolved from CurrentClientContext — nothing here reads a client from user input.
///
/// Connect (no manual webhook/connectionId steps for the client):
///   1. getMe validates the token and gives the bot's id + username
///   2. reuse the client's channel_integrations row (or mint its id) — that id IS the connectionId
///   3. generate a random webhook secret
///   4. setWebhook({PublicBaseUrl}/api/integrations/telegram/webhook/{connectionId}, secret_token)
///   5. only then persist (token, secret, bot id/username, status=connected, webhook_status=active)
/// Anything that fails before step 5 leaves the previous state untouched, so a bad token can't
/// leave a half-connected row behind.
/// </summary>
public interface ITelegramIntegrationService
{
    /// <summary>Throws ArgumentException (malformed token), TelegramApiException (Telegram rejected the
    /// token / webhook) or InvalidOperationException (no public URL configured, or the bot is already
    /// connected to a different client). Messages are safe to show to staff and never contain the token.</summary>
    Task<ChannelIntegrationResponse> ConnectAsync(Guid clientId, string? botToken, CancellationToken ct = default);

    /// <summary>Asks Telegram (getWebhookInfo) whether the webhook is still registered to us and
    /// records the answer.</summary>
    Task<ChannelIntegrationResponse> RefreshStatusAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>deleteWebhook (best effort), then deactivate and drop the stored token + secret.
    /// Contacts, conversations and messages are untouched.</summary>
    Task DisconnectAsync(Guid clientId, CancellationToken ct = default);
}
