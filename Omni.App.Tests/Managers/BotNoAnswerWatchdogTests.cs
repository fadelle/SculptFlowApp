using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Business.Jobs;
using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Entities.Dtos.PlatformAdmin;
using Omni.App.Persistence.Contracts.PlatformAdmin;

namespace Omni.App.Tests.Managers;

/// <summary>QA t9-04: a bot that was asked and never answered hands the conversation to the team, traced as bot.no_answer.</summary>
public class BotNoAnswerWatchdogTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly Mock<IMessageTraceAdminRepository> _traces = new();
    private readonly Mock<IConversationService> _conversations = new();
    private readonly Mock<IMessageTracer> _tracer = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly List<(string Step, string? TraceId)> _steps = [];

    public BotNoAnswerWatchdogTests()
    {
        _config.SetupGet(c => c.AiNoAnswerHandoffMinutes).Returns(10);
        _tracer.Setup(t => t.Step(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TraceIds>(), It.IsAny<double?>(), It.IsAny<object?>()))
            .Callback((string step, string _, TraceIds _, double? _, object? _) => _steps.Add((step, Activity.Current?.TraceId.ToHexString())));
        _conversations.Setup(c => c.HandOverUnansweredAiAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private BotNoAnswerWatchdog Sut()
    {
        var provider = new ServiceCollection()
            .AddScoped(_ => _traces.Object).AddScoped(_ => _conversations.Object).AddScoped(_ => _tracer.Object)
            .BuildServiceProvider();
        return new BotNoAnswerWatchdog(provider.GetRequiredService<IServiceScopeFactory>(), _config.Object, new ManualTimeProvider(Now),
            NullLogger<BotNoAnswerWatchdog>.Instance);
    }

    private static UnansweredRow Row(string? outcome, string mode = ConversationMode.Ai, string? trace = "4bf92f3577b34da6a3ce929d0e0e4736") =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Glow", "Ann", "whatsapp", mode, Now.AddMinutes(-15), Guid.NewGuid(), "text", trace, outcome, null);

    [Fact]
    public async Task Only_an_ai_conversation_whose_bot_never_answered_is_handed_over_in_its_own_trace()
    {
        var silent = Row(TraceResults.NoReply);
        _traces.Setup(t => t.ListUnansweredReadOnlyAsync(Now.AddHours(-2), Now.AddMinutes(-10), null, 0, 200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([silent, Row(TraceResults.NoReply, ConversationMode.Human), Row(TraceResults.InProgress), Row(TraceResults.Skipped),
                Row(TraceResults.NoReply, trace: null)], 5));

        Assert.Equal(1, await Sut().RunOnceAsync(default));

        _conversations.Verify(c => c.HandOverUnansweredAiAsync(silent.ClientId, silent.ConversationId, It.IsAny<CancellationToken>()), Times.Once);
        _conversations.Verify(c => c.HandOverUnansweredAiAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal([(TraceSteps.BotNoAnswer, silent.TraceId)], _steps);
    }

    [Fact]
    public async Task Zero_minutes_turns_the_check_off()
    {
        _config.SetupGet(c => c.AiNoAnswerHandoffMinutes).Returns(0);
        Assert.Equal(0, await Sut().RunOnceAsync(default));
        _traces.Verify(t => t.ListUnansweredReadOnlyAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<Guid?>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
