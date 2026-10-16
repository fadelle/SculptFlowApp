using Omni.App.Entities.Models;

namespace Omni.App.Business.Contracts.Managers;

/// <summary>
/// Looks up a client by id — used by the trusted server-to-server surfaces that take clientId
/// explicitly rather than from a logged-in session: the AI agent tool endpoints
/// (Controllers/Integrations/AiController.cs) and the WhatsApp webhook path, which resolve client from Meta's
/// WABA/phone_number_id, not from a browser session at all.
///
/// (There used to be a "default client" lookup here, driven by the Client:DefaultSlug config key, that
/// registration used to attach every new account to one shared client. Registration now creates a
/// new client per signup — see ClientRegistrationService — so that lookup is gone.)
/// </summary>
public interface IClientContext
{
    /// <summary>Looks up a client by id — used by clientId-scoped API endpoints (the AI agent tool
    /// surface, campaigns, etc.) that already have the id explicit.</summary>
    Task<Client?> GetByIdAsync(Guid clientId, CancellationToken ct = default);
}
