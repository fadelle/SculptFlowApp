using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Engines.Billing;

namespace Omni.App.Tests.Engines;

/// <summary>MessageBillingService.ReserveAiReplyAsync: one ai_reply per AI reply, keyed on the message id, charged
/// (platform_funded) by default, refused when it can't be paid, and nothing at all while billing or the switch is off.</summary>
public class AiReplyBillingTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Guid _conversationId = Guid.NewGuid();
    private readonly Guid _messageId = Guid.NewGuid();
    private readonly Mock<IBillingService> _billing = new();
    private readonly Mock<IProviderBillingService> _providerBilling = new();
    private readonly Mock<IConfigManager> _config = new();
    private BillableEvent? _reserved;

    public AiReplyBillingTests()
    {
        _config.SetupGet(c => c.BillingEnabled).Returns(true);
        _config.SetupGet(c => c.BillingChargeAiReplies).Returns(true);
        _providerBilling.Setup(p => p.DefaultFor(MessageBillingKeys.AiChannel, null)).Returns(ProviderBillingResponsibility.PlatformFunded);
        Answer(new UsageResult(UsageOutcome.Reserved, Guid.NewGuid(), 0.02m, null, false));
    }

    private void Answer(UsageResult result) =>
        _billing.Setup(b => b.ReserveAsync(It.IsAny<BillableEvent>(), It.IsAny<CancellationToken>()))
            .Callback<BillableEvent, CancellationToken>((e, _) => _reserved = e).ReturnsAsync(result);

    private MessageBillingService Sut() => new(_billing.Object, [], _providerBilling.Object, new Mock<IBillingUnitOfWorkFactory>().Object,
        TimeProvider.System, NullLogger<MessageBillingService>.Instance, _config.Object);

    [Fact]
    public async Task An_ai_reply_is_reserved_as_one_charged_ai_reply_keyed_on_its_message()
    {
        var hold = await Sut().ReserveAiReplyAsync(_clientId, _conversationId, _messageId);

        Assert.Equal(new MessageBillingHold(_clientId, $"ai:reply:{_messageId}", BillingSettlementTrigger.OnProviderAccepted), hold);
        Assert.Equal((BillableEventTypes.AiReply, MessageBillingKeys.AiChannel, 1m, ProviderBillingResponsibility.PlatformFunded, true),
            (_reserved!.EventType, _reserved.Channel, _reserved.Quantity, _reserved.ProviderBilling, _reserved.ChargesUsage));
        Assert.Equal((_clientId, _messageId, _conversationId, BillingSource.System), (_reserved.ClientId, _reserved.MessageId, _reserved.ConversationId, _reserved.Source));
    }

    [Theory]
    [InlineData(UsageFailureReason.InsufficientFunds)]
    [InlineData(UsageFailureReason.RateNotFound)]
    public async Task A_refused_reservation_throws_with_its_reason(string reason)
    {
        Answer(new UsageResult(UsageOutcome.Failed, Guid.NewGuid(), null, reason, false));
        var ex = await Assert.ThrowsAsync<BillingDeniedException>(() => Sut().ReserveAiReplyAsync(_clientId, _conversationId, _messageId));
        Assert.Equal(reason, ex.Reason);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Nothing_is_reserved_while_billing_or_ai_reply_charging_is_off(bool billingEnabled, bool chargeAiReplies)
    {
        _config.SetupGet(c => c.BillingEnabled).Returns(billingEnabled);
        _config.SetupGet(c => c.BillingChargeAiReplies).Returns(chargeAiReplies);
        Assert.Null(await Sut().ReserveAiReplyAsync(_clientId, _conversationId, _messageId));
        _billing.Verify(b => b.ReserveAsync(It.IsAny<BillableEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_uncharged_ai_arrangement_is_only_recorded_and_never_blocks_the_reply()
    {
        _providerBilling.Setup(p => p.DefaultFor(MessageBillingKeys.AiChannel, null)).Returns(ProviderBillingResponsibility.CustomerDirect);
        _billing.Setup(b => b.ReserveAsync(It.IsAny<BillableEvent>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        var hold = await Sut().ReserveAiReplyAsync(_clientId, _conversationId, _messageId);

        Assert.NotNull(hold);
    }
}
