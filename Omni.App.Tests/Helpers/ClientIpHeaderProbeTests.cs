using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Omni.App.Common.Configs;
using Omni.App.Middleware;

namespace Omni.App.Tests.Helpers;

/// <summary>QA t12-fix-04: the deploy check of the visitor-IP header is one look at the startup log.</summary>
public class ClientIpHeaderProbeTests
{
    private sealed class ListLogger : ILogger<ClientIpHeaderProbeMiddleware>
    {
        public List<(LogLevel Level, string Text)> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter) =>
            Lines.Add((level, formatter(state, ex)));
    }

    private static (ClientIpHeaderProbeMiddleware Probe, ListLogger Log) Sut(string? header)
    {
        var log = new ListLogger();
        return (new ClientIpHeaderProbeMiddleware(_ => Task.CompletedTask, log, Options.Create(new ProxyOptions { ClientIpHeader = header })), log);
    }

    [Fact]
    public void A_missing_configured_header_is_a_warning_naming_what_arrives()
    {
        var (probe, log) = Sut("CF-Connecting-IP");
        probe.Probe(new HeaderDictionary { ["X-Forwarded-For"] = "203.0.113.9, 10.0.0.1" });
        var (level, text) = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("X-Forwarded-For", text);
        Assert.DoesNotContain("203.0.113.9", text); // names only, never addresses
    }

    [Fact]
    public void The_configured_header_present_is_information()
    {
        var (probe, log) = Sut("CF-Connecting-IP");
        probe.Probe(new HeaderDictionary { ["CF-Connecting-IP"] = "203.0.113.9" });
        Assert.Equal(LogLevel.Information, Assert.Single(log.Lines).Level);
    }

    [Fact]
    public async Task It_probes_only_once_per_start()
    {
        ClientIpHeaderProbeMiddleware.Reset();
        var (probe, log) = Sut(null);
        await probe.InvokeAsync(new DefaultHttpContext());
        await probe.InvokeAsync(new DefaultHttpContext());
        Assert.Single(log.Lines);
    }
}
