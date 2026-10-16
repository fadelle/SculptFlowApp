using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Requests.Contacts;
using Omni.App.Entities.Responses.Contacts;

namespace Omni.App.Controllers.Client;

/// <summary>
/// Contact intake and management. Dashboard actions (List/GetById/Update/UpdateStatus) require login
/// and resolve clientId server-side via DashboardApiController — never from the browser. Create is
/// the n8n intake path (see its own doc comment): no logged-in user, so it takes ClientId in the body
/// and is trusted only through the X-Ingest-Key header.
/// </summary>
[ApiController]
[Route("api/contacts")]
public class ContactsController : DashboardApiController
{
    private readonly IContactService _contacts;

    public ContactsController(IContactService contacts, ICurrentClientContext clientContext) : base(clientContext)
    {
        _contacts = contacts;
    }

    /// <summary>Creates a contact, or returns the existing one if ExternalContactId was already seen for
    /// this client — safe for n8n to retry without creating duplicates. Intake-only: no logged-in user
    /// is calling this, so ClientId comes from the request body, which is why the caller must hold the
    /// ingest key (without it anyone could add contacts to any client). No key = 401.</summary>
    [AllowAnonymous]
    [RequireIngestKey]
    [HttpPost]
    public async Task<ActionResult<ContactResponse>> Create([FromBody] CreateContactRequest request, CancellationToken ct)
    {
        var (contact, wasCreated) = await _contacts.CreateOrGetAsync(request, ct);
        return wasCreated
            ? CreatedAtAction(nameof(GetById), new { id = contact.Id }, contact)
            : Ok(contact);
    }

    [HttpGet]
    public async Task<ActionResult<object>> List(
        [FromQuery] string? stage,
        [FromQuery] Guid? serviceId,
        [FromQuery] string? search,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 200);
        var (items, totalCount) = await _contacts.ListAsync(clientId.Value, stage, serviceId, search, skip, take, ct);
        return Ok(new { items, totalCount, skip, take });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ContactResponse>> GetById(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var contact = await _contacts.GetByIdAsync(clientId.Value, id, ct);
        return contact is null ? NotFound() : Ok(contact);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ContactResponse>> Update(Guid id, [FromBody] UpdateContactRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var contact = await _contacts.UpdateAsync(clientId.Value, id, request, ct);
        return contact is null ? NotFound() : Ok(contact);
    }

    [HttpPost("{id:guid}/stage")]
    public async Task<ActionResult<ContactResponse>> UpdateStage(Guid id, [FromBody] UpdateContactStageRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var contact = await _contacts.UpdateStageAsync(clientId.Value, id, request, ct);
        return contact is null ? NotFound() : Ok(contact);
    }
}
