namespace Omni.App.Tests.Support;

/// <summary>An AI bot for tests that need one answering: Inbox settings that give every channel's account <see cref="BotId"/>,
/// and a runtime that hands out its configuration.</summary>
internal static class BotKit
{
    public static readonly Guid BotId = Guid.Parse("0000000b-0000-0000-0000-00000000b075");

    public static readonly AiBotConfig Config = new(BotId, "Front desk", "booking", "Your name: Front desk\n...", ["knowledge", "bookings"],
        "on_request_or_unsure", null);

    public static Mock<IInboxSettingsService> Inbox(string? blockReason = null)
    {
        var inbox = new Mock<IInboxSettingsService>();
        inbox.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountAiDecision(Guid.NewGuid(), BotId, blockReason, true, true));
        return inbox;
    }

    public static Mock<IBotRuntimeService> Runtime()
    {
        var runtime = new Mock<IBotRuntimeService>();
        runtime.Setup(r => r.ConfigAsync(It.IsAny<Guid>(), BotId, It.IsAny<CancellationToken>())).ReturnsAsync(Config);
        return runtime;
    }
}
