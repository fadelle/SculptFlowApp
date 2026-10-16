using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Entities.Requests.Catalog;
using Omni.App.Entities.Responses.Catalog;

namespace Omni.App.Controllers.Client;

[ApiController]
[Route("api/services")]
public class ServicesController : DashboardApiController
{
    private readonly ICatalogService _services;

    public ServicesController(ICatalogService services, ICurrentClientContext clientContext) : base(clientContext)
    {
        _services = services;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServiceResponse>>> List([FromQuery] bool activeOnly = true, CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var items = await _services.ListAsync(clientId.Value, activeOnly, ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ServiceResponse>> GetById(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var service = await _services.GetByIdAsync(clientId.Value, id, ct);
        return service is null ? NotFound() : Ok(service);
    }

    [HttpPost]
    public async Task<ActionResult<ServiceResponse>> Create([FromBody] CreateServiceRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var service = await _services.CreateAsync(request with { ClientId = clientId.Value }, ct);
        return CreatedAtAction(nameof(GetById), new { id = service.Id }, service);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ServiceResponse>> Update(Guid id, [FromBody] UpdateServiceRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var service = await _services.UpdateAsync(clientId.Value, id, request, ct);
        return service is null ? NotFound() : Ok(service);
    }

    /// <summary>Activate/deactivate — services are never deleted (leads, appointments and service
    /// bookings reference them).</summary>
    [HttpPost("{id:guid}/active")]
    public async Task<ActionResult<ServiceResponse>> SetActive(Guid id, [FromBody] SetServiceActiveRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var service = await _services.SetActiveAsync(clientId.Value, id, request.IsActive, ct);
        return service is null ? NotFound() : Ok(service);
    }
}
