using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlasticSurgery.Business.Contracts.Services.Caching;
using PlasticSurgery.Controllers.Filters;

namespace PlasticSurgery.Controllers.Admin;

/// <summary>
/// Platform-admin cache API (internal; called by the SculptFlowAdmin portal's Cache page). Keys can contain ':' so they
/// are passed as a query parameter. GET keys?prefix= lists keys; GET key?key= returns one value; DELETE key?key= removes
/// one; DELETE keys?prefix= removes a prefix; DELETE (no path) clears everything and reloads settings.
/// Errors: 400 missing key/prefix, 404 key not cached.
/// </summary>
[ApiController]
[AllowAnonymous]
[RequirePlatformAdminKey]
[Route("api/platform-admin/cache")]
[ApiErrors]
public class CacheAdminController : ControllerBase
{
    private readonly ICacheAdminService _cache;

    public CacheAdminController(ICacheAdminService cache)
    {
        _cache = cache;
    }

    [HttpGet("keys")]
    public async Task<IActionResult> List([FromQuery] string? prefix, CancellationToken ct) => Ok(await _cache.ListAsync(prefix, ct));

    [HttpGet("key")]
    public async Task<IActionResult> Get([FromQuery] string key, CancellationToken ct) => Ok(await _cache.GetAsync(key, ct));

    [HttpDelete("key")]
    public async Task<IActionResult> Remove([FromQuery] string key, CancellationToken ct)
    {
        await _cache.RemoveAsync(key, ct);
        return NoContent();
    }

    [HttpDelete("keys")]
    public async Task<IActionResult> RemoveByPrefix([FromQuery] string prefix, CancellationToken ct) =>
        Ok(await _cache.RemoveByPrefixAsync(prefix, ct));

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken ct) => Ok(await _cache.ClearAsync(ct));
}
