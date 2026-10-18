using Microsoft.AspNetCore.Mvc;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Knowledge;
using Omni.App.Entities.Requests.Knowledge;
using Omni.App.Entities.Responses.Knowledge;
using Omni.App.Controllers.Filters;

namespace Omni.App.Controllers.Client;

/// <summary>
/// Website sources for the Knowledge Base. Login required; the client ALWAYS comes from the logged-in user
/// (CurrentClientContext) — never from the request. This controller only starts/manages crawls; the crawling
/// itself runs in the background (see WebsiteScrapeWorker), so POST returns immediately with a pending run.
/// </summary>
[ApiController]
[Route("api/knowledge/websites")]
public class WebsiteSourcesController : DashboardApiController
{
    private readonly IWebsiteSourceService _websites;

    public WebsiteSourcesController(IWebsiteSourceService websites, ICurrentClientContext clientContext) : base(clientContext)
    {
        _websites = websites;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WebsiteSourceResponse>>> List(CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        return Ok(await _websites.ListAsync(clientId.Value, ct));
    }

    /// <summary>Adds a website and queues its first crawl (202 Accepted — the crawl is not finished yet).</summary>
    [HttpPost]
    public async Task<ActionResult<WebsiteSourceResponse>> Create([FromBody] CreateWebsiteSourceRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        var source = await _websites.CreateAsync(clientId.Value, request, ct);
        return AcceptedAtAction(nameof(GetById), new { id = source.Id }, source);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WebsiteSourceDetailResponse>> GetById(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        var detail = await _websites.GetAsync(clientId.Value, id, ct);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpGet("{id:guid}/pages")]
    public async Task<ActionResult<IReadOnlyList<WebsitePageResponse>>> Pages(
        Guid id, [FromQuery] string? status, [FromQuery] int skip = 0, [FromQuery] int take = 200, CancellationToken ct = default)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        if (await _websites.GetAsync(clientId.Value, id, ct) is null) return NotFound();
        return Ok(await _websites.ListPagesAsync(clientId.Value, id, status, Math.Max(skip, 0), take, ct));
    }

    [HttpPost("{id:guid}/rescrape")]
    public async Task<ActionResult<WebsiteSourceResponse>> Rescrape(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        try
        {
            var source = await _websites.RescrapeAsync(clientId.Value, id, ct);
            return source is null ? NotFound() : Accepted(source);
        }
        catch (ArgumentException ex)
        {
            return Conflict(new { error = ApiErrorsAttribute.Localize(HttpContext, ex) });
        }
    }

    [HttpPost("{id:guid}/active")]
    public async Task<ActionResult<WebsiteSourceResponse>> SetActive(Guid id, [FromBody] SetKnowledgeActiveRequest request, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();
        var source = await _websites.SetActiveAsync(clientId.Value, id, request.IsActive, ct);
        return source is null ? NotFound() : Ok(source);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var clientId = await GetClientIdAsync(ct);
        if (clientId is null) return Forbid();

        try
        {
            return await _websites.DeleteAsync(clientId.Value, id, ct) ? NoContent() : NotFound();
        }
        catch (ArgumentException ex)
        {
            return Conflict(new { error = ApiErrorsAttribute.Localize(HttpContext, ex) });
        }
    }
}
