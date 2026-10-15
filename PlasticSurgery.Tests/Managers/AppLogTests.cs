using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PlasticSurgery.Business.Contracts.Services.Monitoring;
using PlasticSurgery.Business.Jobs;
using PlasticSurgery.Business.Managers;
using PlasticSurgery.Business.Mappers.PlatformAdmin;
using PlasticSurgery.Business.Providers.Logging;
using PlasticSurgery.Business.Services.Monitoring;
using PlasticSurgery.Business.Services.PlatformAdmin;
using PlasticSurgery.Controllers.Admin;
using PlasticSurgery.Controllers.Filters;
using PlasticSurgery.Entities.Dtos.Monitoring;
using PlasticSurgery.Persistence.Contracts.Monitoring;

namespace PlasticSurgery.Tests.Managers;

internal static class AppLogKit
{
    public static readonly DateTimeOffset Start = new(2026, 10, 15, 12, 0, 10, TimeSpan.Zero);

    public static AppLog Entry(string fingerprint = "fp", string message = "m", DateTimeOffset? at = null, string level = AppLogLevels.Error) => new()
    {
        OccurredAt = at ?? Start,
        Level = level,
        Category = "Test",
        Message = message,
        Fingerprint = fingerprint
    };

    public static AppLogCaptureSettings Settings(bool enabled = true, LogLevel minimum = LogLevel.Warning, int cap = 20, params string[] excluded) =>
        new(enabled, minimum, excluded, cap);
}

/// <summary>The in-memory buffer: capture rules, the per-fingerprint cap, the bounded queue and the suppression scope.</summary>
public class AppLogCollectorTests
{
    private readonly AppLogCollector _sut = new();

    [Fact]
    public void Captures_only_enabled_levels_at_or_above_the_minimum_and_not_excluded_loggers()
    {
        Assert.True(_sut.ShouldCapture(LogLevel.Warning, "PlasticSurgery.X"));
        Assert.False(_sut.ShouldCapture(LogLevel.Information, "PlasticSurgery.X"));
        Assert.False(_sut.ShouldCapture(LogLevel.None, "PlasticSurgery.X"));

        _sut.Apply(AppLogKit.Settings(minimum: LogLevel.Error, excluded: "Microsoft.EntityFrameworkCore"));
        Assert.False(_sut.ShouldCapture(LogLevel.Warning, "PlasticSurgery.X"));
        Assert.True(_sut.ShouldCapture(LogLevel.Critical, "PlasticSurgery.X"));
        Assert.False(_sut.ShouldCapture(LogLevel.Error, "Microsoft.EntityFrameworkCore.Update"));

        _sut.Apply(AppLogKit.Settings(enabled: false));
        Assert.False(_sut.ShouldCapture(LogLevel.Critical, "PlasticSurgery.X"));
    }

    [Fact]
    public async Task Suppress_stops_capture_for_the_current_flow_only_until_disposed()
    {
        using (_sut.Suppress())
        {
            Assert.False(_sut.ShouldCapture(LogLevel.Error, "X"));
            await Task.Yield();
            Assert.False(_sut.ShouldCapture(LogLevel.Error, "X"));
            Assert.False(await Task.Run(() => _sut.ShouldCapture(LogLevel.Error, "X"))); // work started inside flows with it
        }
        Assert.True(_sut.ShouldCapture(LogLevel.Error, "X"));

        var other = Task.CompletedTask;
        using (_sut.Suppress())
        {
            // A flow started elsewhere (not from inside the scope) still captures.
            using (ExecutionContext.SuppressFlow()) other = Task.Run(() => Assert.True(_sut.ShouldCapture(LogLevel.Error, "X")));
        }
        await other;
    }

    [Fact]
    public void Drain_returns_entries_oldest_first_up_to_the_batch_size()
    {
        for (var i = 0; i < 5; i++) _sut.Add(AppLogKit.Entry("fp" + i, "m" + i));

        Assert.Equal(["m0", "m1", "m2"], _sut.Drain(AppLogKit.Start, 3).Select(e => e.Message));
        Assert.Equal(2, _sut.Pending);
        Assert.Equal(["m3", "m4"], _sut.Drain(AppLogKit.Start, 10).Select(e => e.Message));
        Assert.Empty(_sut.Drain(AppLogKit.Start, 10));
    }

    [Fact]
    public void Beyond_the_cap_a_minute_of_one_fingerprint_becomes_one_repeat_row()
    {
        _sut.Apply(AppLogKit.Settings(cap: 3));
        for (var i = 1; i <= 10; i++) _sut.Add(AppLogKit.Entry(message: "n" + i, at: AppLogKit.Start.AddSeconds(i)));
        _sut.Add(AppLogKit.Entry("other", "x"));

        // The minute is still running: only the first three (and the other fingerprint) are written so far.
        Assert.Equal(["n1", "n2", "n3", "x"], _sut.Drain(AppLogKit.Start.AddSeconds(20), 100).Select(e => e.Message));

        var summary = Assert.Single(_sut.Drain(AppLogKit.Start.AddMinutes(1), 100));
        Assert.Equal(("n10", 7), (summary.Message, summary.RepeatCount));

        // The next minute starts a fresh count.
        _sut.Add(AppLogKit.Entry(message: "later", at: AppLogKit.Start.AddMinutes(2)));
        Assert.Equal("later", Assert.Single(_sut.Drain(AppLogKit.Start.AddMinutes(2), 100)).Message);
    }

    [Fact]
    public void An_entry_in_a_new_minute_closes_the_previous_minute_first()
    {
        _sut.Apply(AppLogKit.Settings(cap: 1));
        _sut.Add(AppLogKit.Entry(message: "a"));
        _sut.Add(AppLogKit.Entry(message: "b"));
        _sut.Add(AppLogKit.Entry(message: "c"));
        _sut.Add(AppLogKit.Entry(message: "next", at: AppLogKit.Start.AddMinutes(1)));

        var rows = _sut.Drain(AppLogKit.Start.AddMinutes(1), 100);
        Assert.Equal(["a", "c", "next"], rows.Select(e => e.Message));
        Assert.Equal([1, 2, 1], rows.Select(e => e.RepeatCount));
    }

    [Fact]
    public void A_full_buffer_drops_entries_and_reports_how_many_once()
    {
        _sut.Apply(AppLogKit.Settings(cap: 1000));
        for (var i = 0; i < AppLogLimits.BufferCapacity + 25; i++) _sut.Add(AppLogKit.Entry("fp" + i));

        var rows = _sut.Drain(AppLogKit.Start, AppLogLimits.BufferCapacity + 100);
        Assert.Equal(AppLogLimits.BufferCapacity, rows.Count);

        var dropped = Assert.Single(_sut.Drain(AppLogKit.Start, 10));
        Assert.Equal((AppLogLimits.DroppedCategory, AppLogLevels.Warning, 25), (dropped.Category, dropped.Level, dropped.RepeatCount));
        Assert.Contains("25 log entries were not saved", dropped.Message);
        Assert.Empty(_sut.Drain(AppLogKit.Start, 10));
    }

    [Fact]
    public void Requeued_entries_are_written_again_without_the_cap()
    {
        _sut.Apply(AppLogKit.Settings(cap: 1));
        _sut.Requeue([AppLogKit.Entry(message: "a"), AppLogKit.Entry(message: "b")]);
        Assert.Equal(["a", "b"], _sut.Drain(AppLogKit.Start, 10).Select(e => e.Message));
    }

    [Fact]
    public async Task Concurrent_adds_are_never_lost()
    {
        _sut.Apply(AppLogKit.Settings(cap: 1000));
        var drained = new List<AppLog>();
        var writers = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++) _sut.Add(AppLogKit.Entry($"w{w}-{i % 10}"));
        })).ToArray();
        while (!writers.All(w => w.IsCompleted)) drained.AddRange(_sut.Drain(AppLogKit.Start, 100));
        await Task.WhenAll(writers);
        drained.AddRange(_sut.Drain(AppLogKit.Start, 10_000));

        Assert.Equal(4000, drained.Sum(e => e.RepeatCount));
    }

    [Fact]
    public void Default_settings_match_the_declared_defaults()
    {
        var defaults = AppLogCaptureSettings.Default;
        Assert.Equal((true, LogLevel.Warning, 20), (defaults.Enabled, defaults.MinimumLevel, defaults.MaxPerFingerprintPerMinute));
        Assert.Empty(defaults.ExcludedCategoryPrefixes);

        var parsed = AppLogCaptureSettings.From(true, "nonsense", " A.B , ,C ", 0);
        Assert.Equal((LogLevel.Warning, 1), (parsed.MinimumLevel, parsed.MaxPerFingerprintPerMinute));
        Assert.Equal(["A.B", "C"], parsed.ExcludedCategoryPrefixes);
        Assert.Equal(LogLevel.Critical, AppLogCaptureSettings.From(true, "CRITICAL", null, 5).MinimumLevel);
    }
}

/// <summary>What a log call turns into: text, template, exception, fingerprint, ids, trace, route and masked values.</summary>
public class DatabaseLoggerTests
{
    private readonly AppLogCollector _collector = new();
    private readonly HttpContextAccessor _http = new();
    private readonly LoggerExternalScopeProvider _scopes = new();

    private ILogger Logger(string category = "PlasticSurgery.Business.Test")
    {
        var provider = new DatabaseLoggerProvider(_collector, _http, new ManualTimeProvider(AppLogKit.Start));
        provider.SetScopeProvider(_scopes);
        return provider.CreateLogger(category);
    }

    private AppLog Single() => Assert.Single(_collector.Drain(AppLogKit.Start, 100));

    private static Exception Thrown(Func<Exception> make)
    {
        try { throw make(); }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public void A_warning_keeps_its_text_template_ids_and_other_values_with_secrets_masked()
    {
        var conversationId = Guid.NewGuid();
        Logger().LogWarning("n8n returned {StatusCode} for conversation {ConversationId} (token {ApiToken})", 502, conversationId, "abc123");
        Logger().LogInformation("not saved");

        var row = Single();
        Assert.Equal(AppLogLevels.Warning, row.Level);
        Assert.Equal("PlasticSurgery.Business.Test", row.Category);
        Assert.Equal($"n8n returned 502 for conversation {conversationId} (token abc123)", row.Message);
        Assert.Equal("n8n returned {StatusCode} for conversation {ConversationId} (token {ApiToken})", row.MessageTemplate);
        Assert.Equal(conversationId, row.ConversationId);
        Assert.Equal(AppLogKit.Start, row.OccurredAt);
        Assert.Equal(AppLogLimits.InstanceName, row.Instance);
        var properties = JsonSerializer.Deserialize<Dictionary<string, string>>(row.Properties)!;
        Assert.Equal("502", properties["StatusCode"]);
        Assert.Equal("***", properties["ApiToken"]);
        Assert.False(properties.ContainsKey("ConversationId")); // stored in its own column
        Assert.Null(row.ExceptionType);
    }

    [Fact]
    public void An_exception_is_stored_and_the_fingerprint_ignores_the_values()
    {
        var first = Thrown(() => new TimeoutException("db slow"));
        Logger().LogError(first, "Send failed for {MessageId}", Guid.NewGuid());
        Logger().LogError(Thrown(() => new TimeoutException("other text")), "Send failed for {MessageId}", Guid.NewGuid());
        Logger().LogError(Thrown(() => new InvalidOperationException("x")), "Send failed for {MessageId}", Guid.NewGuid());
        Logger("PlasticSurgery.Other").LogError(Thrown(() => new TimeoutException("db slow")), "Send failed for {MessageId}", Guid.NewGuid());

        var rows = _collector.Drain(AppLogKit.Start, 100);
        Assert.Equal("System.TimeoutException", rows[0].ExceptionType);
        Assert.Equal("db slow", rows[0].ExceptionMessage);
        Assert.Contains(nameof(Thrown), rows[0].StackTrace);
        Assert.NotNull(rows[0].MessageId);
        Assert.Equal(16, rows[0].Fingerprint.Length);
        Assert.Equal(rows[0].Fingerprint, rows[1].Fingerprint);
        Assert.NotEqual(rows[0].Fingerprint, rows[2].Fingerprint); // another exception type
        Assert.NotEqual(rows[0].Fingerprint, rows[3].Fingerprint); // another logger
    }

    [Fact]
    public void Trace_id_route_method_and_scope_ids_are_attached()
    {
        var clinicId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/conversations/123/messages/send";
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("api/conversations/{id}/messages/send"), 0,
            EndpointMetadataCollection.Empty, "send"));
        _http.HttpContext = context;

        using var activity = new Activity("test").Start();
        using (_scopes.Push(new Dictionary<string, object?> { ["ClinicId"] = clinicId.ToString(), ["RequestPath"] = "/raw/path?token=x", ["ActionName"] = "Send" }))
        {
            Logger().LogError("boom");
        }

        var row = Single();
        Assert.Equal(activity.TraceId.ToHexString(), row.TraceId);
        Assert.Equal(("POST", "/api/conversations/{id}/messages/send"), (row.Method, row.Route));
        Assert.Equal(clinicId, row.ClinicId);
        var properties = JsonSerializer.Deserialize<Dictionary<string, string>>(row.Properties)!;
        Assert.Equal("Send", properties["ActionName"]);
        Assert.False(properties.ContainsKey("RequestPath"));
    }

    [Fact]
    public void Outside_a_request_there_is_no_route_or_trace()
    {
        Activity.Current = null;
        Logger().LogCritical("worker died");
        var row = Single();
        Assert.Equal(AppLogLevels.Critical, row.Level);
        Assert.Null(row.Route);
        Assert.Null(row.Method);
        Assert.Null(row.TraceId);
        Assert.Equal("{}", row.Properties);
    }

    [Fact]
    public void A_message_without_a_template_uses_its_text_for_the_fingerprint()
    {
        var logger = Logger();
        logger.Log(LogLevel.Error, new EventId(7, "SendFailed"), "plain state", null, (s, _) => s);
        logger.Log(LogLevel.Error, default, "other state", null, (s, _) => s);

        var rows = _collector.Drain(AppLogKit.Start, 100);
        Assert.Null(rows[0].MessageTemplate);
        Assert.Equal("plain state", rows[0].Message);
        Assert.NotEqual(rows[0].Fingerprint, rows[1].Fingerprint);
        Assert.Equal("SendFailed", JsonSerializer.Deserialize<Dictionary<string, string>>(rows[0].Properties)!["EventId"]);
    }

    [Fact]
    public void Long_text_is_capped_and_many_values_are_truncated()
    {
        var huge = new string('x', AppLogLimits.MaxMessageLength * 2);
        var values = Enumerable.Range(0, 40).Select(i => (object?)new string('v', 400)).ToArray();
        var template = string.Join(" ", Enumerable.Range(0, 40).Select(i => "{P" + i + "}"));
        Logger().LogError(template, values);
        Logger().LogError(huge);

        var rows = _collector.Drain(AppLogKit.Start, 100);
        Assert.True(rows[0].Properties.Length <= AppLogLimits.MaxPropertiesLength + 200);
        Assert.Contains("_truncated", rows[0].Properties);
        Assert.Equal(AppLogLimits.MaxMessageLength, rows[1].Message.Length);
    }

    [Fact]
    public void Disabled_suppressed_or_failing_capture_never_reaches_the_caller()
    {
        _collector.Apply(AppLogKit.Settings(enabled: false));
        Logger().LogError("off");
        _collector.Apply(AppLogKit.Settings());
        using (_collector.Suppress()) Logger().LogError("suppressed");
        Assert.Empty(_collector.Drain(AppLogKit.Start, 100));

        var failing = new Mock<IAppLogCollector>();
        failing.Setup(c => c.ShouldCapture(It.IsAny<LogLevel>(), It.IsAny<string>())).Returns(true);
        failing.Setup(c => c.Add(It.IsAny<AppLog>())).Throws(new InvalidOperationException("broken"));
        var logger = new DatabaseLoggerProvider(failing.Object, _http, TimeProvider.System).CreateLogger("X");
        logger.LogError("still fine"); // no exception
        Assert.Null(logger.BeginScope("scope"));
        new DatabaseLoggerProvider(failing.Object, _http, TimeProvider.System).Dispose();
    }

    [Fact]
    public void Top_frame_drops_the_file_and_line()
    {
        Assert.Null(AppLogFingerprint.TopFrame(null));
        Assert.Null(AppLogFingerprint.TopFrame(new Exception("never thrown")));
        var frame = AppLogFingerprint.TopFrame(Thrown(() => new Exception("x")))!;
        Assert.StartsWith("at ", frame);
        Assert.DoesNotContain(" in ", frame);
        Assert.Equal(AppLogFingerprint.Compute("a", "t", null, null), AppLogFingerprint.Compute("a", "t", null, null));
        Assert.NotEqual(AppLogFingerprint.Compute("a", "t", null, null), AppLogFingerprint.Compute("a", "t2", null, null));
    }

    [Fact]
    public void Levels_parse_and_filter_as_a_minimum()
    {
        Assert.Equal([AppLogLevels.Error, AppLogLevels.Critical], AppLogLevels.AtOrAbove(LogLevel.Error));
        Assert.Equal(AppLogLevels.All, AppLogLevels.AtOrAbove(LogLevel.Warning));
        Assert.Null(AppLogLevels.Parse("info"));
        Assert.Null(AppLogLevels.NameOf(LogLevel.Information));
    }
}

/// <summary>Saving, trimming and flushing: settings copied each flush, batches, retries, purge timing.</summary>
public class AppLogServiceAndJobTests
{
    private static readonly DateTimeOffset Now = AppLogKit.Start;

    [Fact]
    public async Task Save_writes_rows_and_skips_an_empty_batch()
    {
        var repo = new Mock<IAppLogRepository>();
        var unit = new Mock<IUnitOfWork>();
        var sut = new AppLogService(repo.Object, unit.Object, Mock.Of<IConfigManager>(), new ManualTimeProvider(Now));

        await sut.SaveAsync([], default);
        unit.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        var row = AppLogKit.Entry();
        await sut.SaveAsync([row], default);
        repo.Verify(r => r.AddRange(It.Is<IEnumerable<AppLog>>(rows => rows.Single() == row)), Times.Once);
        unit.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Purge_uses_the_warning_and_error_retention_settings()
    {
        var repo = new Mock<IAppLogRepository>();
        repo.Setup(r => r.DeleteOlderThanAsync(It.Is<IReadOnlyCollection<string>>(l => l.SequenceEqual(new[] { "Warning" })), Now.AddDays(-14), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4).Verifiable();
        repo.Setup(r => r.DeleteOlderThanAsync(It.Is<IReadOnlyCollection<string>>(l => l.SequenceEqual(new[] { "Error", "Critical" })), Now.AddDays(-90), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1).Verifiable();
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.ErrorLogWarningRetentionDays).Returns(14);
        config.SetupGet(c => c.ErrorLogErrorRetentionDays).Returns(90);

        Assert.Equal(5, await new AppLogService(repo.Object, Mock.Of<IUnitOfWork>(), config.Object, new ManualTimeProvider(Now)).PurgeAsync(default));
        repo.Verify();
    }

    private sealed record JobKit(AppLogFlushJob Job, Mock<IAppLogService> Service, AppLogCollector Collector, Mock<IConfigManager> Config,
        ManualTimeProvider Clock, List<string> Logged);

    private static JobKit Job()
    {
        var clock = new ManualTimeProvider(Now);
        var collector = new AppLogCollector();
        var service = new Mock<IAppLogService>();
        var config = new Mock<IConfigManager>();
        config.SetupGet(c => c.ErrorLogEnabled).Returns(true);
        config.SetupGet(c => c.ErrorLogMinimumLevel).Returns("Warning");
        config.SetupGet(c => c.ErrorLogExcludedCategories).Returns("");
        config.SetupGet(c => c.ErrorLogMaxPerFingerprintPerMinute).Returns(1000);
        var services = new ServiceCollection().AddScoped(_ => service.Object).BuildServiceProvider();
        var logged = new List<string>();
        // The job's own logger goes through the error log too, so the suppression scope is what keeps it out.
        var logger = new CapturingLogger(collector, logged);
        var job = new AppLogFlushJob(collector, config.Object, services.GetRequiredService<IServiceScopeFactory>(), clock, logger);
        return new JobKit(job, service, collector, config, clock, logged);
    }

    [Fact]
    public async Task Flush_copies_the_settings_writes_everything_and_purges_once_per_interval()
    {
        var kit = Job();
        kit.Config.SetupGet(c => c.ErrorLogMinimumLevel).Returns("error");
        kit.Config.SetupGet(c => c.ErrorLogExcludedCategories).Returns("Noisy");
        for (var i = 0; i < AppLogLimits.BatchSize + 3; i++) kit.Collector.Add(AppLogKit.Entry("fp" + i));

        await kit.Job.FlushAsync(default);

        Assert.Equal((LogLevel.Error, "Noisy"), (kit.Collector.Settings.MinimumLevel, kit.Collector.Settings.ExcludedCategoryPrefixes.Single()));
        kit.Service.Verify(s => s.SaveAsync(It.Is<IReadOnlyCollection<AppLog>>(r => r.Count == AppLogLimits.BatchSize), It.IsAny<CancellationToken>()), Times.Once);
        kit.Service.Verify(s => s.SaveAsync(It.Is<IReadOnlyCollection<AppLog>>(r => r.Count == 3), It.IsAny<CancellationToken>()), Times.Once);
        kit.Service.Verify(s => s.PurgeAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(0, kit.Collector.Pending);

        kit.Clock.Advance(TimeSpan.FromMinutes(1));
        await kit.Job.FlushAsync(default); // nothing waiting, no purge due: no database work
        kit.Service.Verify(s => s.SaveAsync(It.IsAny<IReadOnlyCollection<AppLog>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));

        kit.Clock.Advance(TimeSpan.FromMinutes(AppLogLimits.PurgeIntervalMinutes));
        await kit.Job.FlushAsync(default);
        kit.Service.Verify(s => s.PurgeAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task A_failed_write_puts_the_batch_back_and_its_warning_is_not_saved()
    {
        var kit = Job();
        kit.Service.Setup(s => s.SaveAsync(It.IsAny<IReadOnlyCollection<AppLog>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("db down"));
        kit.Collector.Add(AppLogKit.Entry());

        await kit.Job.FlushAsync(default);

        Assert.Single(kit.Logged); // logged to the console...
        Assert.Equal(1, kit.Collector.Pending); // ...the batch waits for a retry, and the warning about it was not queued
        kit.Service.Verify(s => s.PurgeAsync(It.IsAny<CancellationToken>()), Times.Never);

        kit.Service.Setup(s => s.SaveAsync(It.IsAny<IReadOnlyCollection<AppLog>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        await kit.Job.FlushAsync(default);
        Assert.Equal(0, kit.Collector.Pending);

        kit.Service.Setup(s => s.SaveAsync(It.IsAny<IReadOnlyCollection<AppLog>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());
        kit.Collector.Add(AppLogKit.Entry());
        await Assert.ThrowsAsync<OperationCanceledException>(() => kit.Job.FlushAsync(default));
    }

    [Fact]
    public async Task A_failed_purge_is_logged_not_thrown()
    {
        var kit = Job();
        kit.Service.Setup(s => s.PurgeAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("slow"));
        await kit.Job.FlushAsync(default);
        Assert.Single(kit.Logged);
        Assert.Equal(0, kit.Collector.Pending);
    }

    [Fact]
    public async Task Stopping_writes_what_is_left()
    {
        var kit = Job();
        kit.Collector.Add(AppLogKit.Entry());
        await kit.Job.StopAsync(default);
        kit.Service.Verify(s => s.SaveAsync(It.Is<IReadOnlyCollection<AppLog>>(r => r.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>A job logger that both records what it was given and passes it through the error log like the real one.</summary>
    private sealed class CapturingLogger(AppLogCollector collector, List<string> logged) : ILogger<AppLogFlushJob>
    {
        private readonly ILogger _database = new DatabaseLoggerProvider(collector, new HttpContextAccessor(), TimeProvider.System).CreateLogger("Job");

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) logged.Add(formatter(state, exception));
            _database.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}

/// <summary>The platform-admin reads: level/sort parsing, clamping, filters passed through, 404, summary counts.</summary>
public class AppLogAdminTests
{
    private static readonly DateTimeOffset Now = AppLogKit.Start;
    private readonly Mock<IAppLogRepository> _repo = new();
    private readonly Mock<IConfigManager> _config = new();

    private AppLogAdminService Sut()
    {
        _config.SetupGet(c => c.ErrorLogEnabled).Returns(true);
        _config.SetupGet(c => c.ErrorLogMinimumLevel).Returns("Warning");
        _config.SetupGet(c => c.ErrorLogWarningRetentionDays).Returns(14);
        _config.SetupGet(c => c.ErrorLogErrorRetentionDays).Returns(90);
        return new AppLogAdminService(_repo.Object, _config.Object, new ManualTimeProvider(Now));
    }

    [Fact]
    public async Task Groups_pass_a_minimum_level_filter_and_report_the_settings()
    {
        var clinicId = Guid.NewGuid();
        var group = new AppLogGroupRow("fp", "Error", "C", "t", "m", null, 5, Now, Now, 9, null);
        _repo.Setup(r => r.ListGroupsReadOnlyAsync(
                It.Is<AppLogFilter>(f => f.From == Now.AddHours(-24) && f.Levels.SequenceEqual(new[] { "Error", "Critical" }) && f.ClinicId == clinicId && f.Search == "meta"),
                true, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([group], 3, 42L)).Verifiable();

        var report = await Sut().GroupsAsync(24, " ERROR ", clinicId, " meta ", "Count", 10, default);

        _repo.Verify();
        Assert.Equal(("error", "count", 42L, 3), (report.Level, report.Sort, report.TotalOccurrences, report.GroupCount));
        Assert.Equal((true, "Warning", 14, 90), (report.Enabled, report.MinimumLevel, report.WarningRetentionDays, report.ErrorRetentionDays));
        Assert.Equal((Now.AddHours(-24), Now, 24), (report.From, report.To, report.Hours));
        Assert.Same(group, Assert.Single(report.Groups));
    }

    [Fact]
    public async Task Defaults_clamping_and_bad_input()
    {
        _repo.Setup(r => r.ListGroupsReadOnlyAsync(It.IsAny<AppLogFilter>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(([], 0, 0L));
        var sut = Sut();

        var report = await sut.GroupsAsync(1_000_000, null, null, "  ", null, 1_000_000, default);
        Assert.Equal(("warning", "recent", AppLogLimits.MaxReportHours), (report.Level, report.Sort, report.Hours));
        _repo.Verify(r => r.ListGroupsReadOnlyAsync(It.Is<AppLogFilter>(f => f.Levels.Count == 3 && f.Search == null), false, AppLogLimits.MaxGroupTake,
            It.IsAny<CancellationToken>()));
        Assert.Equal(1, (await sut.GroupsAsync(-3, null, null, null, null, 5, default)).Hours);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.GroupsAsync(24, "info", null, null, null, 5, default));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.GroupsAsync(24, null, null, null, "oldest", 5, default));
        await Assert.ThrowsAsync<ArgumentException>(() => sut.ListAsync(24, "debug", null, null, null, null, 1, 10, default));
    }

    [Fact]
    public async Task List_pages_and_filters_by_fingerprint_and_trace()
    {
        var row = new AppLogRow(1, Now, "Error", "C", "m", null, "fp", 1, "abc", null, null, null, null, null);
        _repo.Setup(r => r.ListReadOnlyAsync(It.Is<AppLogFilter>(f => f.Fingerprint == "abcd" && f.TraceId == "ef01" && f.Levels.Count == 1), 40, 20,
            It.IsAny<CancellationToken>())).ReturnsAsync(([row], 61)).Verifiable();

        var page = await Sut().ListAsync(24, "critical", null, null, " ABCD ", "EF01", 3, 20, default);

        _repo.Verify();
        Assert.Equal((3, 20, 61), (page.Page, page.PageSize, page.Total));
        Assert.Same(row, Assert.Single(page.Items));

        _repo.Setup(r => r.ListReadOnlyAsync(It.IsAny<AppLogFilter>(), 0, AppLogLimits.MaxPageSize, It.IsAny<CancellationToken>())).ReturnsAsync(([], 0)).Verifiable();
        Assert.Equal(1, (await Sut().ListAsync(24, null, null, null, null, null, 0, 100_000, default)).Page);
        _repo.Verify();
    }

    [Fact]
    public async Task Get_maps_the_row_and_a_missing_id_is_not_found()
    {
        _repo.Setup(r => r.GetReadOnlyAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(new AppLog
        {
            Id = 5, Level = "Error", Category = "C", Message = "m", Fingerprint = "fp", StackTrace = "stack", Properties = "{\"A\":\"1\",\"B\":null,\"C\":2}"
        });
        var detail = await Sut().GetAsync(5, default);
        Assert.Equal((5L, "stack"), (detail.Id, detail.StackTrace));
        Assert.Equal("1", detail.Properties["A"]);
        Assert.Null(detail.Properties["B"]);
        Assert.Equal("2", detail.Properties["C"]);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().GetAsync(6, default));
    }

    [Fact]
    public void Unreadable_properties_map_to_empty()
    {
        Assert.Empty(AppLogMapper.Properties(null));
        Assert.Empty(AppLogMapper.Properties("not json"));
        Assert.Empty(AppLogMapper.Properties("[1,2]"));
    }

    [Fact]
    public async Task Summary_counts_each_level_and_the_latest_error()
    {
        _repo.Setup(r => r.CountByLevelSinceReadOnlyAsync(Now.AddHours(-24), It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new AppLogLevelCount("Warning", 12, Now),
            new AppLogLevelCount("Error", 3, Now.AddHours(-2)),
            new AppLogLevelCount("Critical", 1, Now.AddHours(-5))
        ]);
        var summary = await Sut().SummaryAsync(24, default);
        Assert.Equal((12L, 3L, 1L, Now.AddHours(-2)), (summary.Warnings, summary.Errors, summary.Criticals, summary.LastErrorAt!.Value));

        _repo.Setup(r => r.CountByLevelSinceReadOnlyAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AppLogLevelCount("Warning", 2, Now)]);
        var quiet = await Sut().SummaryAsync(0, default);
        Assert.Equal((1, 0L, (DateTimeOffset?)null), (quiet.Hours, quiet.Errors, quiet.LastErrorAt));
    }

    [Fact]
    public async Task Controller_applies_defaults_for_missing_query_values()
    {
        var service = new Mock<IAppLogAdminService>();
        var controller = new AppLogsAdminController(service.Object);

        Assert.IsType<OkObjectResult>(await controller.Groups(0, null, null, null, null, 0, default));
        service.Verify(s => s.GroupsAsync(AppLogLimits.DefaultReportHours, null, null, null, null, AppLogLimits.DefaultGroupTake, It.IsAny<CancellationToken>()));

        Assert.IsType<OkObjectResult>(await controller.List(0, "error", null, null, "fp", null, 0, 0, default));
        service.Verify(s => s.ListAsync(AppLogLimits.DefaultReportHours, "error", null, null, "fp", null, 1, AppLogLimits.DefaultPageSize, It.IsAny<CancellationToken>()));

        Assert.IsType<OkObjectResult>(await controller.Get(4, default));
        service.Verify(s => s.GetAsync(4, It.IsAny<CancellationToken>()));

        Assert.IsType<OkObjectResult>(await controller.Summary(6, default));
        service.Verify(s => s.SummaryAsync(6, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public void Provider_failures_mapped_by_ApiErrors_are_logged_as_errors_and_client_errors_are_not()
    {
        var logged = new List<(LogLevel Level, Exception? Exception)>();
        var factory = new Mock<ILoggerFactory>();
        var logger = new Mock<ILogger>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        logger.Setup(l => l.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(i => logged.Add(((LogLevel)i.Arguments[0], (Exception?)i.Arguments[3]))));
        factory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
        var services = new ServiceCollection().AddSingleton(factory.Object).BuildServiceProvider();

        ExceptionContext Context(Exception exception) =>
            new(new ActionContext(new DefaultHttpContext { RequestServices = services }, new RouteData(),
                new ControllerActionDescriptor { DisplayName = "Send" }), [])
            {
                Exception = exception
            };

        var sendFailed = new ChannelSendException("Meta said no");
        new ApiErrorsAttribute().OnException(Context(sendFailed));
        new ApiErrorsAttribute().OnException(Context(new ArgumentException("bad input")));
        new ApiErrorsAttribute().OnException(new ExceptionContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [])
        {
            Exception = new ChannelSendException("no services") // no request services: still mapped, just not logged
        });

        var entry = Assert.Single(logged);
        Assert.Equal((LogLevel.Error, (Exception?)sendFailed), entry);
    }
}
