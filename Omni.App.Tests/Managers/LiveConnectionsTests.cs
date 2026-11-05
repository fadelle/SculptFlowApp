using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Jobs;
using Omni.App.Business.Managers;
using Omni.App.Persistence.Contracts.Clients;

namespace Omni.App.Tests.Managers;

/// <summary>QA t11-auto-02: a deactivated member's open Inbox kept receiving the client's live updates.</summary>
public class LiveConnectionsTests
{
    private readonly LiveConnections _live = new();
    private readonly List<string> _aborted = [];
    private readonly Guid _clientA = Guid.NewGuid(), _clientB = Guid.NewGuid();

    private void Open(string connection, string user, Guid client, ClaimsPrincipal? principal = null) =>
        _live.Add(connection, user, client, principal ?? new ClaimsPrincipal(), () => _aborted.Add(connection));

    [Fact]
    public void Closing_a_user_or_a_client_aborts_only_their_connections()
    {
        Open("c1", "ann", _clientA);
        Open("c2", "ann", _clientA);   // a second tab
        Open("c3", "bob", _clientA);
        Open("c4", "cat", _clientB);

        Assert.Equal(2, _live.CloseUser("ann"));
        Assert.Equal(["c1", "c2"], _aborted);
        Assert.Equal(1, _live.CloseClient(_clientA));
        Assert.Equal(["c1", "c2", "c3"], _aborted);
        Assert.Equal("c4", Assert.Single(_live.List()).ConnectionId);
        Assert.Equal(0, _live.CloseUser("ann"));   // already closed: nothing aborted twice
    }

    [Fact]
    public void A_disconnected_connection_is_forgotten()
    {
        Open("c1", "ann", _clientA);
        _live.Remove("c1");
        Assert.Empty(_live.List());
        Assert.False(_live.Close("c1"));
        Assert.Empty(_aborted);
    }

    [Fact]
    public async Task The_periodic_check_closes_connections_whose_access_ended()
    {
        Open("still-ok", "ann", _clientA);
        Open("removed", "bob", _clientA);   // membership gone: no access at all
        Open("moved", "cat", _clientA);     // now a member of another client
        var clients = new Mock<IClientRepository>();
        clients.Setup(c => c.GetUserAccessAsync("ann", It.IsAny<CancellationToken>())).ReturnsAsync(new UserAccess("ann", _clientA, UserRole.Member, null, "Ann", null));
        clients.Setup(c => c.GetUserAccessAsync("cat", It.IsAny<CancellationToken>())).ReturnsAsync(new UserAccess("cat", _clientB, UserRole.Member, null, "Cat", null));
        var services = new ServiceCollection().AddSingleton(clients.Object).BuildServiceProvider();
        var job = new LiveConnectionCheckJob(services.GetRequiredService<IServiceScopeFactory>(), _live, NullLogger<LiveConnectionCheckJob>.Instance);

        Assert.Equal(2, await job.RunOnceAsync(default));
        Assert.Equal(["moved", "removed"], _aborted.Order(StringComparer.Ordinal));
        Assert.Equal("still-ok", Assert.Single(_live.List()).ConnectionId);
        Assert.Equal(0, await job.RunOnceAsync(default));
    }
}
