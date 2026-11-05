using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Contracts.Engines.WhatsApp;
using Omni.App.Business.Contracts.HttpClients.N8n;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Engines.WhatsApp;
using Omni.App.Business.Jobs;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.WhatsApp;
using Omni.App.Persistence.Contracts.Channels;

namespace Omni.App.Tests.Engines;

/// <summary>QA t10-10: the webhook stores a delivery and answers at once; the inbox processes it later, in order.</summary>
public class WhatsAppWebhookInboxTests
{
    private readonly Mock<IWhatsAppDeliveryRepository> _deliveries = new();
    private readonly Mock<IMetaWebhookProcessor> _processor = new();
    private readonly Mock<IAiTriggerNotifier> _ai = new();
    private readonly Mock<IWhatsAppDeliverySignal> _signal = new();
    private readonly WhatsAppWebhookDelivery _delivery = new() { Id = 7, Body = "{\"entry\":[]}", Attempts = 1, TraceParent = null };

    private WhatsAppWebhookInbox Sut() => new(_deliveries.Object, _processor.Object, _ai.Object, _signal.Object, Mock.Of<IMessageTracer>(),
        TimeProvider.System, NullLogger<WhatsAppWebhookInbox>.Instance);

    private void Due() => _deliveries.Setup(d => d.ClaimNextAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(_delivery);

    private void Processed(params WhatsAppWebhookResponse[] responses) =>
        _processor.Setup(p => p.ProcessAllAsync(It.IsAny<JsonElement>(), It.IsAny<CancellationToken>())).ReturnsAsync(responses);

    [Fact]
    public async Task Enqueue_stores_the_body_with_its_hash_and_wakes_the_worker_once()
    {
        var body = Encoding.UTF8.GetBytes("{\"a\":1}");
        var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)).ToLowerInvariant();
        _deliveries.Setup(d => d.EnqueueAsync("{\"a\":1}", expectedHash, It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Assert.True(await Sut().EnqueueAsync(body));
        _signal.Verify(s => s.Notify(), Times.Once);

        // Meta redelivered the same body: nothing new to process, no wake-up.
        _deliveries.Setup(d => d.EnqueueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Assert.False(await Sut().EnqueueAsync(body));
        _signal.Verify(s => s.Notify(), Times.Once);
    }

    [Fact]
    public async Task Nothing_due_processes_nothing()
    {
        Assert.False(await Sut().ProcessNextAsync());
        _processor.Verify(p => p.ProcessAllAsync(It.IsAny<JsonElement>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_due_delivery_is_processed_completed_then_the_ai_runs_once_per_conversation()
    {
        Due();
        var client = Guid.NewGuid(); var c1 = Guid.NewGuid(); var c2 = Guid.NewGuid();
        WhatsAppWebhookResponse Msg(Guid convo, string text) => new(true, "customer_message", true, client, ConversationId: convo, MessageType: "text", Content: text, AdContext: text == "c" ? "ad" : null);
        Processed(Msg(c1, "a"), Msg(c2, "b"), Msg(c1, "c"), new WhatsAppWebhookResponse(true, "message_status", false, client));

        Assert.True(await Sut().ProcessNextAsync());
        _deliveries.Verify(d => d.CompleteAsync(7, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _ai.Verify(a => a.NotifyAsync(It.Is<AiTriggerPayload>(p => p.ConversationId == c1 && p.MessageText == "c" && p.AdContext == "ad" && p.Channel == "whatsapp"), It.IsAny<CancellationToken>()), Times.Once);
        _ai.Verify(a => a.NotifyAsync(It.Is<AiTriggerPayload>(p => p.ConversationId == c2 && p.MessageText == "b"), It.IsAny<CancellationToken>()), Times.Once);
        _ai.Verify(a => a.NotifyAsync(It.IsAny<AiTriggerPayload>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task One_failed_ai_trigger_doesnt_stop_the_others_or_the_delivery()
    {
        Due();
        var client = Guid.NewGuid(); var c1 = Guid.NewGuid(); var c2 = Guid.NewGuid();
        Processed(new(true, "customer_message", true, client, ConversationId: c1, Content: "hi"), new(true, "customer_message", true, client, ConversationId: c2, Content: "hi"));
        _ai.Setup(a => a.NotifyAsync(It.Is<AiTriggerPayload>(p => p.ConversationId == c1), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("n8n down"));
        Assert.True(await Sut().ProcessNextAsync());
        _ai.Verify(a => a.NotifyAsync(It.Is<AiTriggerPayload>(p => p.ConversationId == c2), It.IsAny<CancellationToken>()), Times.Once);
        _deliveries.Verify(d => d.CompleteAsync(7, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_delivery_that_fails_as_a_whole_is_tried_again_later_then_given_up()
    {
        Due();
        _processor.Setup(p => p.ProcessAllAsync(It.IsAny<JsonElement>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
        var before = DateTimeOffset.UtcNow;
        Assert.True(await Sut().ProcessNextAsync());
        _deliveries.Verify(d => d.RetryLaterAsync(7, It.Is<string>(e => e.Contains("db down")),
            It.Is<DateTimeOffset>(at => at >= before.AddSeconds(9) && at <= DateTimeOffset.UtcNow.AddSeconds(11)), It.IsAny<CancellationToken>()), Times.Once);
        _ai.Verify(a => a.NotifyAsync(It.IsAny<AiTriggerPayload>(), It.IsAny<CancellationToken>()), Times.Never);

        _delivery.Attempts = IWhatsAppWebhookInbox.MaxAttempts;
        Assert.True(await Sut().ProcessNextAsync());
        _deliveries.Verify(d => d.FailAsync(7, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _deliveries.Verify(d => d.CompleteAsync(It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 30)]
    [InlineData(3, 90)]
    [InlineData(4, 270)]
    public void Retries_back_off(int attempts, int seconds) => Assert.Equal(TimeSpan.FromSeconds(seconds), WhatsAppWebhookInbox.RetryDelay(attempts));

    [Fact]
    public async Task The_worker_drains_every_due_delivery_and_stops_when_none_is_left()
    {
        var inbox = new Mock<IWhatsAppWebhookInbox>();
        inbox.SetupSequence(i => i.ProcessNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(true).ReturnsAsync(false);
        var services = new ServiceCollection().AddScoped(_ => inbox.Object).BuildServiceProvider();
        var worker = new WhatsAppDeliveryWorker(services.GetRequiredService<IServiceScopeFactory>(), new WhatsAppDeliverySignal(), NullLogger<WhatsAppDeliveryWorker>.Instance);
        Assert.Equal(2, await worker.DrainAsync(default));

        // A claim that throws (database unreachable) ends the pass instead of spinning.
        inbox.Setup(i => i.ProcessNextAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
        Assert.Equal(0, await worker.DrainAsync(default));
    }

    [Fact]
    public async Task The_signal_wakes_one_waiter_and_otherwise_times_out()
    {
        var signal = new WhatsAppDeliverySignal();
        signal.Notify();
        signal.Notify(); // a second notify before anyone waits is the same wake-up
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(1), default));
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(50), default));
    }
}
