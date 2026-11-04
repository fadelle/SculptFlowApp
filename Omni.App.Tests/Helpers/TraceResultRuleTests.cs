namespace Omni.App.Tests.Helpers;

/// <summary>QA t7-10: what a message trace came to, from its steps.</summary>
public class TraceResultRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static TraceFacts Facts(int accepted = 0, int modeChanged = 0, bool handoff = false, int triggered = 0, int saved = 0,
        string? failed = null, string? skip = null, string? skipDetail = null, int minutesAgo = 30) =>
        new(accepted, modeChanged, handoff, triggered, saved, failed, skip, skipDetail, Now.AddMinutes(-minutesAgo));

    [Fact]
    public void A_handoff_wins_then_a_reply_then_a_failure_then_a_skip()
    {
        Assert.Equal(TraceResults.HandedOff, TraceResultRule.Of(Facts(accepted: 1, handoff: true, failed: "bot.run_failed"), Now).Outcome);
        Assert.Equal(TraceResults.HandedOff, TraceResultRule.Of(Facts(modeChanged: 1), Now).Outcome);
        Assert.Equal(TraceResults.Replied, TraceResultRule.Of(Facts(accepted: 1, failed: "send.provider_failed"), Now).Outcome);
        Assert.Equal((TraceResults.Failed, "send.provider_failed"), TraceResultRule.Of(Facts(failed: "send.provider_failed", skip: "bot.skipped"), Now));
        Assert.Equal((TraceResults.Skipped, "conversation_in_human_mode"),
            TraceResultRule.Of(Facts(skip: "bot.skipped", skipDetail: """{"reason":"conversation_in_human_mode"}"""), Now));
        Assert.Equal((TraceResults.Skipped, "send.blocked"), TraceResultRule.Of(Facts(skip: "send.blocked", skipDetail: "not json"), Now));
    }

    [Fact]
    public void A_bot_that_was_asked_and_never_answered_is_no_reply_after_a_few_minutes()
    {
        Assert.Equal(TraceResults.InProgress, TraceResultRule.Of(Facts(triggered: 1, minutesAgo: 2), Now).Outcome);
        Assert.Equal(TraceResults.NoReply, TraceResultRule.Of(Facts(triggered: 1, minutesAgo: 6), Now).Outcome);
        Assert.Equal(TraceResults.Received, TraceResultRule.Of(Facts(saved: 1), Now).Outcome);
        Assert.Equal(TraceResults.Other, TraceResultRule.Of(Facts(), Now).Outcome);
    }
}
