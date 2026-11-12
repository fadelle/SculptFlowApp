using Microsoft.Extensions.Options;
using Omni.App.Common.Configs;
using Omni.App.Common.Helpers;
using Omni.App.Controllers.Filters;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contracts.Monitoring;

namespace Omni.App.Middleware;

/// <summary>
/// Records every write to the platform-admin API (any method but GET/HEAD/OPTIONS under /api/platform-admin) in
/// activity.platform_admin_writes, after it answered: who it said it was (X-Admin-Actor), from which address, the route,
/// the status and the client it names (QA t6-admin-23: a direct call with the shared key left no audit row, since only the
/// portal audits). Refused calls (401, 404 for an off API) are recorded too: they are the ones worth seeing. Written in its
/// own scope after the response, so the write itself can never be affected; a failure to record is a warning. Rows older
/// than <see cref="RetentionDays"/> are deleted now and then.
/// </summary>
public sealed class PlatformAdminWriteLogMiddleware
{
    public const int RetentionDays = 365;
    private static readonly PathString Api = "/api/platform-admin";

    private readonly RequestDelegate _next;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PlatformAdminWriteLogMiddleware> _logger;
    private readonly string? _ipHeader;

    public PlatformAdminWriteLogMiddleware(RequestDelegate next, IServiceScopeFactory scopes, ILogger<PlatformAdminWriteLogMiddleware> logger,
        IOptions<ProxyOptions> proxy)
    {
        _next = next;
        _scopes = scopes;
        _logger = logger;
        _ipHeader = proxy.Value.ClientIpHeader;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(Api) || HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method))
        {
            await _next(context);
            return;
        }
        try
        {
            await _next(context);
        }
        finally
        {
            await RecordAsync(context);
        }
    }

    private async Task RecordAsync(HttpContext context)
    {
        try
        {
            var path = context.Request.Path.Value ?? "";
            var clientId = context.GetRouteValue("clientId") is { } value && Guid.TryParse(value.ToString(), out var id) ? id : (Guid?)null;
            await using var scope = _scopes.CreateAsyncScope();
            var rows = scope.ServiceProvider.GetRequiredService<IPlatformAdminWriteRepository>();
            await rows.AddAsync(new PlatformAdminWrite
            {
                OccurredAt = DateTimeOffset.UtcNow,
                Actor = RequirePlatformAdminKeyAttribute.Actor(context.Request),
                IpAddress = ClientIp.Resolve(context, _ipHeader),
                Method = context.Request.Method,
                Path = path.Length > 300 ? path[..300] : path,
                StatusCode = context.Response.StatusCode,
                ClientId = clientId
            }, CancellationToken.None);
            if (Random.Shared.Next(500) == 0) await rows.DeleteOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-RetentionDays), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A platform-admin write ({Method} {Path}) wasn't recorded.", context.Request.Method, context.Request.Path.Value);
        }
    }
}
