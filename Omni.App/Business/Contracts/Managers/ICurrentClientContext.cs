using Microsoft.AspNetCore.Http;
using Omni.App.Entities.Models;

namespace Omni.App.Business.Contracts.Managers;

/// <summary>
/// Resolves "the current client" for every dashboard Razor Page and dashboard-facing API — from
/// the authenticated Identity user via client_users, never from a clientId the browser supplies.
/// MVP assumes one active membership per user (see ClientUser's doc comment): the first matching
/// row wins if there's ever more than one.
///
/// Used everywhere except the WhatsApp/Telegram webhook paths, which resolve the client from the
/// stored channel connection instead (see Business/Engines/WhatsApp/MetaWebhookProcessor.cs and
/// Controllers/Integrations/TelegramWebhookController.cs). New-user registration creates the client AND its first
/// client_users row itself (see ClientRegistrationService).
/// </summary>
public interface ICurrentClientContext
{
    Task<Client?> GetClientAsync(CancellationToken ct = default);

    Task<Guid?> GetClientIdAsync(CancellationToken ct = default);
}
