using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PlasticSurgery.Business.Jobs;
using PlasticSurgery.Business.Managers;
using PlasticSurgery.Hubs;

namespace PlasticSurgery.Tests.Managers;

public class EventLoggerTests
{
    [Fact]
    public void Log_adds_a_complete_event_row_with_defaults()
    {
        var repo = new Mock<IEventLogRepository>();
        EventLog? added = null;
        repo.Setup(r => r.Add(It.IsAny<EventLog>())).Callback<EventLog>(e => added = e);
        var clinic = Guid.NewGuid(); var lead = Guid.NewGuid(); var convo = Guid.NewGuid(); var appt = Guid.NewGuid();
        new EventLogger(repo.Object).Log(clinic, "lead_created", lead, convo, appt, "n8n", "{\"a\":1}");
        Assert.Equal((clinic, lead, convo, appt, "lead_created", "n8n", "{\"a\":1}"), (added!.ClinicId, added.LeadId, added.ConversationId, added.AppointmentId, added.EventType, added.Source, added.Metadata));
        Assert.NotEqual(Guid.Empty, added.Id);

        new EventLogger(repo.Object).Log(clinic, "x", metadataJson: "  ");
        Assert.Equal("{}", added.Metadata);
    }
}

public class ClinicContextTests
{
    private readonly Mock<IClinicRepository> _clinics = new();

    private static IHttpContextAccessor Accessor(ClaimsPrincipal? user) => new HttpContextAccessor { HttpContext = user is null ? null : new DefaultHttpContext { User = user } };

    private static ClaimsPrincipal SignedIn(string id) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "test"));

    [Fact]
    public async Task Anonymous_or_missing_contexts_have_no_clinic()
    {
        Assert.Null(await new CurrentClinicContext(_clinics.Object, Accessor(null)).GetClinicIdAsync());
        Assert.Null(await new CurrentClinicContext(_clinics.Object, Accessor(new ClaimsPrincipal(new ClaimsIdentity()))).GetClinicAsync());
        Assert.Null(await new CurrentClinicContext(_clinics.Object, Accessor(new ClaimsPrincipal(new ClaimsIdentity([], "test")))).GetClinicIdAsync()); // authenticated, no id claim
        _clinics.Verify(c => c.GetClinicIdOfActiveMemberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Signed_in_users_resolve_through_their_active_membership()
    {
        var id = Guid.NewGuid();
        var clinic = new Clinic { Id = id };
        _clinics.Setup(c => c.GetClinicIdOfActiveMemberAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(id);
        _clinics.Setup(c => c.GetClinicOfActiveMemberAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
        var ctx = new CurrentClinicContext(_clinics.Object, Accessor(SignedIn("u1")));
        Assert.Equal(id, await ctx.GetClinicIdAsync());
        Assert.Same(clinic, await ctx.GetClinicAsync());
    }

    [Fact]
    public async Task Clinic_context_loads_by_id()
    {
        var clinic = new Clinic { Id = Guid.NewGuid() };
        _clinics.Setup(c => c.GetAsync(clinic.Id, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
        Assert.Same(clinic, await new ClinicContext(_clinics.Object).GetByIdAsync(clinic.Id));
    }
}

public class InboxNotifierTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IClientProxy> _proxy = new();
    private readonly InboxNotifier _sut;
    private string? _group;

    public InboxNotifierTests()
    {
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Callback<string>(g => _group = g).Returns(_proxy.Object);
        var hub = new Mock<IHubContext<InboxHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        _sut = new InboxNotifier(hub.Object);
    }

    private void Sent(string method) =>
        _proxy.Verify(p => p.SendCoreAsync(method, It.Is<object?[]>(a => a.Length == 1), It.IsAny<CancellationToken>()), Times.Once);

    [Fact]
    public async Task Every_notification_goes_to_the_clinic_group_with_its_event_name()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await _sut.NewMessageAsync(_clinicId, id, id, id, "inbound", "lead", "whatsapp_customer", now);
        Assert.Equal(InboxHub.ClinicGroup(_clinicId), _group);
        await _sut.MessageStatusUpdatedAsync(_clinicId, id, id, "read", now);
        await _sut.ConversationUpdatedAsync(_clinicId, id);
        await _sut.NotificationCreatedAsync(_clinicId, new NotificationResponse(id, "t", "title", null, null, null, null, null, false, now));
        await _sut.AppointmentChangedAsync(_clinicId, id, "created");
        await _sut.ConversationModeChangedAsync(_clinicId, id, "human");
        await _sut.WhatsAppTemplateUpdatedAsync(_clinicId, id, "n", "approved", null, now);
        await _sut.WhatsAppHealthUpdatedAsync(_clinicId, id, "healthy", null, null, null, null, null, now);
        foreach (var m in new[] { "NewMessage", "MessageStatusUpdated", "ConversationUpdated", "NotificationCreated", "AppointmentChanged", "ConversationModeChanged", "WhatsAppTemplateUpdated", "WhatsAppHealthUpdated" }) Sent(m);
    }

    [Fact]
    public void Group_names_are_per_clinic()
    {
        var other = Guid.NewGuid();
        Assert.NotEqual(InboxHub.ClinicGroup(_clinicId), InboxHub.ClinicGroup(other));
        Assert.Contains(_clinicId.ToString("N"), InboxHub.ClinicGroup(_clinicId).Replace("-", ""));
    }
}

public class QueueAndWorkerTests
{
    [Fact]
    public async Task Queues_are_fifo_and_wait_for_work()
    {
        foreach (var queue in new (Action<Guid> Enqueue, Func<CancellationToken, ValueTask<Guid>> Dequeue)[]
                 {
                     (new WebsiteScrapeQueue().Enqueue, new WebsiteScrapeQueue().DequeueAsync),
                 })
        {
            Assert.NotNull(queue.Enqueue);
        }

        var q = new WebsiteScrapeQueue();
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        q.Enqueue(a); q.Enqueue(b);
        Assert.Equal(a, await q.DequeueAsync(CancellationToken.None));
        Assert.Equal(b, await q.DequeueAsync(CancellationToken.None));

        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await q.DequeueAsync(cts.Token));

        var bq = new KnowledgeBenchmarkRunQueue();
        bq.Enqueue(a);
        Assert.Equal(a, await bq.DequeueAsync(CancellationToken.None));
    }

    private static async Task<bool> Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        return condition();
    }

    [Fact]
    public async Task Website_scrape_worker_recovers_interrupted_runs_then_processes_the_queue_and_survives_failures()
    {
        var websites = new Mock<IWebsiteSourceRepository>();
        var uow = new Mock<IUnitOfWork>();
        var processor = new Mock<IWebsiteScrapeProcessor>();
        var crawling = new KnowledgeWebsiteScrapeRun { Id = Guid.NewGuid(), WebsiteSourceId = Guid.NewGuid(), Status = WebsiteScrapeStatus.Crawling };
        var source = new KnowledgeWebsiteSource { Id = crawling.WebsiteSourceId, Status = WebsiteScrapeStatus.Crawling };
        var pendingId = Guid.NewGuid();
        websites.Setup(w => w.ListRunsInStatusAsync(WebsiteScrapeStatus.Crawling, It.IsAny<CancellationToken>())).ReturnsAsync(new List<KnowledgeWebsiteScrapeRun> { crawling });
        websites.Setup(w => w.GetSourceByIdAsync(source.Id, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        websites.Setup(w => w.ListRunIdsInStatusAsync(WebsiteScrapeStatus.Pending, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Guid> { pendingId });
        var processed = new List<Guid>();
        processor.Setup(p => p.RunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns<Guid, CancellationToken>((id, _) =>
        {
            processed.Add(id);
            return id == pendingId ? Task.FromException(new InvalidOperationException("boom")) : Task.CompletedTask;
        });
        var services = new ServiceCollection();
        services.AddSingleton(websites.Object).AddSingleton(uow.Object).AddSingleton(processor.Object);
        var queue = new WebsiteScrapeQueue();
        var worker = new WebsiteScrapeWorker(queue, services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<WebsiteScrapeWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        var later = Guid.NewGuid();
        queue.Enqueue(later);
        Assert.True(await Eventually(() => processed.Count == 2));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(new[] { pendingId, later }.Order(), processed.Order()); // the failing one didn't stop the loop
        Assert.Equal((WebsiteScrapeStatus.Failed, WebsiteScrapeStatus.Failed), (crawling.Status, source.Status));
        Assert.Contains("restart", crawling.ErrorSummary);
    }

    [Fact]
    public async Task Benchmark_worker_recovers_and_runs_queued_benchmarks()
    {
        var service = new Mock<IKnowledgeBenchmarkService>();
        var recovered = Guid.NewGuid();
        service.Setup(s => s.RecoverInterruptedRunsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<Guid> { recovered });
        var executed = new List<Guid>();
        service.Setup(s => s.ExecuteRunAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns<Guid, CancellationToken>((id, _) => { executed.Add(id); return id == recovered ? Task.FromException(new InvalidOperationException("x")) : Task.CompletedTask; });
        var services = new ServiceCollection();
        services.AddSingleton(service.Object);
        var queue = new KnowledgeBenchmarkRunQueue();
        var worker = new KnowledgeBenchmarkRunWorker(queue, services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<KnowledgeBenchmarkRunWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        var next = Guid.NewGuid();
        queue.Enqueue(next);
        Assert.True(await Eventually(() => executed.Count == 2));
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(new[] { recovered, next }.Order(), executed.Order());
    }

    [Fact]
    public async Task Benchmark_worker_survives_a_failing_recovery()
    {
        var service = new Mock<IKnowledgeBenchmarkService>();
        service.Setup(s => s.RecoverInterruptedRunsAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));
        var services = new ServiceCollection();
        services.AddSingleton(service.Object);
        var worker = new KnowledgeBenchmarkRunWorker(new KnowledgeBenchmarkRunQueue(), services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<KnowledgeBenchmarkRunWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Config_refresh_job_reloads_immediately_and_stops_cleanly()
    {
        var config = new Mock<IConfigManager>();
        var job = new ConfigRefreshJob(config.Object);
        await job.StartAsync(CancellationToken.None);
        Assert.True(await Eventually(() => config.Invocations.Any(i => i.Method.Name == "RefreshAsync")));
        await job.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Billing_maintenance_runs_both_tasks_and_never_throws()
    {
        var subscriptions = new Mock<ISubscriptionService>();
        var messageBilling = new Mock<IMessageBillingService>();
        subscriptions.Setup(s => s.ProcessDueAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);
        messageBilling.Setup(m => m.ResolveStaleReservationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(3);
        var services = new ServiceCollection();
        services.AddSingleton(subscriptions.Object).AddSingleton(messageBilling.Object);
        var worker = new BillingMaintenanceWorker(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<BillingMaintenanceWorker>.Instance, Mock.Of<IConfigManager>());
        await worker.RunOnceAsync(CancellationToken.None);
        subscriptions.Verify(s => s.ProcessDueAsync(It.IsAny<CancellationToken>()), Times.Once);
        messageBilling.Verify(m => m.ResolveStaleReservationsAsync(It.IsAny<CancellationToken>()), Times.Once);

        subscriptions.Setup(s => s.ProcessDueAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));
        await worker.RunOnceAsync(CancellationToken.None); // logged, not thrown

    }

    [Fact]
    public async Task Billing_maintenance_worker_exits_quietly_when_stopped_before_its_first_run()
    {
        var worker = new BillingMaintenanceWorker(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<BillingMaintenanceWorker>.Instance, Mock.Of<IConfigManager>());
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
    }
}
