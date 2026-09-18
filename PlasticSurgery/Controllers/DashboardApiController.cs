using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Services;

namespace PlasticSurgery.Controllers;

/// <summary>
/// Base for every dashboard-facing API controller — everything the browser calls that isn't one of
/// the n8n/trusted-server-to-server surfaces (those stay on RequireIngestKeyAttribute instead; see
/// AiController, MessagesController, WhatsAppIntegrationEventsController). Requires a logged-in
/// session (Identity cookie) and resolves clinicId from ICurrentClinicContext (clinic_users) —
/// never from a clinicId the browser supplies, per the MVP's auth scope decision.
/// </summary>
[Authorize]
public abstract class DashboardApiController : ControllerBase
{
    private readonly ICurrentClinicContext _clinicContext;

    protected DashboardApiController(ICurrentClinicContext clinicContext)
    {
        _clinicContext = clinicContext;
    }

    /// <summary>Resolves the logged-in user's clinicId, or null if they have no active clinic_users
    /// row. [Authorize] on this class already guarantees the caller is logged in, so null here
    /// specifically means "authenticated but not linked to any clinic" — callers should return
    /// Forbid() (403), not Unauthorized() (401 is for "not logged in at all", which the auth
    /// middleware already handles before an action method ever runs).</summary>
    protected Task<Guid?> GetClinicIdAsync(CancellationToken ct) => _clinicContext.GetClinicIdAsync(ct);
}
