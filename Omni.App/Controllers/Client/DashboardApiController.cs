using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Controllers.Filters;

namespace Omni.App.Controllers.Client;

/// <summary>
/// Base for every dashboard-facing API controller — everything the browser calls that isn't one of
/// the n8n/trusted-server-to-server surfaces (those stay on RequireIngestKeyAttribute instead; see
/// AiController, MessagesController, WhatsAppIntegrationEventsController). Requires a logged-in
/// session (Identity cookie) and resolves clientId from ICurrentClientContext (client_users) —
/// never from a clientId the browser supplies, per the MVP's auth scope decision.
/// </summary>
[Authorize]
[ApiErrors]
public abstract class DashboardApiController : ControllerBase
{
    private readonly ICurrentClientContext _clientContext;

    protected DashboardApiController(ICurrentClientContext clientContext)
    {
        _clientContext = clientContext;
    }

    /// <summary>Resolves the logged-in user's clientId, or null if they have no active client_users
    /// row. [Authorize] on this class already guarantees the caller is logged in, so null here
    /// specifically means "authenticated but not linked to any client" — callers should return
    /// Forbid() (403), not Unauthorized() (401 is for "not logged in at all", which the auth
    /// middleware already handles before an action method ever runs).</summary>
    protected Task<Guid?> GetClientIdAsync(CancellationToken ct) => _clientContext.GetClientIdAsync(ct);
}
