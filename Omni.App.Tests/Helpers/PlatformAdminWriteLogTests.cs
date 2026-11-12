using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Omni.App.Common.Configs;
using Omni.App.Entities.Responses.Billing;
using Omni.App.Middleware;
using Omni.App.Persistence.Contracts.Monitoring;

namespace Omni.App.Tests.Helpers;

/// <summary>QA t6-admin-23: a direct call to the platform-admin API with the shared key left no audit row.</summary>
public class PlatformAdminWriteLogTests
{
    private sealed class Rows : IPlatformAdminWriteRepository
    {
        public List<PlatformAdminWrite> Saved { get; } = [];
        public Task AddAsync(PlatformAdminWrite write, CancellationToken ct = default) { Saved.Add(write); return Task.CompletedTask; }
        public Task<PagedResponse<PlatformAdminWrite>> ListAsync(Guid? clientId, int page, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default) => Task.FromResult(0);
    }

    private readonly Rows _rows = new();

    private PlatformAdminWriteLogMiddleware Sut(int status) =>
        new(c => { c.Response.StatusCode = status; return Task.CompletedTask; },
            new ServiceCollection().AddScoped<IPlatformAdminWriteRepository>(_ => _rows).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<PlatformAdminWriteLogMiddleware>.Instance, Options.Create(new ProxyOptions()));

    private static DefaultHttpContext Request(string method, string path, string? actor = null)
    {
        var c = new DefaultHttpContext();
        c.Request.Method = method;
        c.Request.Path = path;
        c.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.9");
        if (actor is not null) c.Request.Headers["X-Admin-Actor"] = actor;
        return c;
    }

    [Fact]
    public async Task A_write_is_recorded_with_who_from_where_and_its_status()
    {
        await Sut(200).InvokeAsync(Request("PUT", "/api/platform-admin/contacts/abc", "ops@omni"));
        var row = Assert.Single(_rows.Saved);
        Assert.Equal(("ops@omni", "203.0.113.9", "PUT", "/api/platform-admin/contacts/abc", 200), (row.Actor, row.IpAddress, row.Method, row.Path, row.StatusCode));
    }

    [Fact]
    public async Task A_refused_write_is_recorded_too_and_reads_and_other_routes_are_not()
    {
        await Sut(401).InvokeAsync(Request("POST", "/api/platform-admin/billing/clients/x/wallet/top-ups"));
        await Sut(200).InvokeAsync(Request("GET", "/api/platform-admin/overview/totals"));
        await Sut(200).InvokeAsync(Request("POST", "/api/contacts"));
        var row = Assert.Single(_rows.Saved);
        Assert.Equal((401, "admin-api"), (row.StatusCode, row.Actor)); // no actor header: the API's own default
    }
}
