using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Models;

namespace Omni.App.Business.Contracts.Managers;

/// <summary>
/// Resolves "the current client" for every dashboard Razor Page and dashboard-facing API — from the signed-in user's
/// role and active client_users row (UserAccess), never from a clientId the browser supplies. Only an active client or
/// member user with an active membership gets one; an admin, a deactivated user or a removed member gets none (and the
/// cookie check in <see cref="Business.Managers.CurrentClientContext.ValidatePrincipalAsync"/> signs them out).
///
/// Used everywhere except the WhatsApp/Telegram webhook paths, which resolve the client from the stored channel
/// connection instead (see Business/Engines/WhatsApp/MetaWebhookProcessor.cs and
/// Controllers/Integrations/TelegramWebhookController.cs). New-user registration creates the client AND its first
/// client_users row itself (see ClientRegistrationService).
/// </summary>
public interface ICurrentClientContext
{
    /// <summary>The signed-in user's access (client, role, language), or null.</summary>
    Task<UserAccess?> GetAccessAsync(CancellationToken ct = default);

    Task<Client?> GetClientAsync(CancellationToken ct = default);

    Task<Guid?> GetClientIdAsync(CancellationToken ct = default);
}
