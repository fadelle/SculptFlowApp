using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Managers;
using Omni.App.Persistence.Contracts.Clients;

namespace Omni.App.Business.Jobs;

/// <summary>
/// Every <see cref="Interval"/>, checks each live Inbox connection against the same rule the cookie check uses on every HTTP
/// request (CurrentClientContext.StillAllowed: an active user with an active membership in that client, and the cookie's
/// security stamp still current) and closes the ones that no longer pass (QA t11-auto-02). Deactivating a member or a client
/// closes their connections at once (MemberService, ClientAdminService); this catches every other way access ends (a removed
/// membership, a locked user, a password change).
/// </summary>
public class LiveConnectionCheckJob : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILiveConnections _connections;
    private readonly ILogger<LiveConnectionCheckJob> _logger;

    public LiveConnectionCheckJob(IServiceScopeFactory scopes, ILiveConnections connections, ILogger<LiveConnectionCheckJob> logger)
    {
        _scopes = scopes;
        _connections = connections;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(Interval, stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>One pass; returns how many connections were closed.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var open = _connections.List();
        if (open.Count == 0) return 0;
        var closed = 0;
        try
        {
            using var scope = _scopes.CreateScope();
            var clients = scope.ServiceProvider.GetRequiredService<IClientRepository>();
            foreach (var connection in open)
            {
                var access = await clients.GetUserAccessAsync(connection.UserId, ct);
                var allowed = access is not null && access.ClientId == connection.ClientId && CurrentClientContext.StillAllowed(access, connection.User);
                if (!allowed && _connections.Close(connection.ConnectionId)) closed++;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Live connection check failed; retrying next interval.");
        }
        if (closed > 0) _logger.LogInformation("Closed {Count} Inbox connection(s) whose access ended.", closed);
        return closed;
    }
}
