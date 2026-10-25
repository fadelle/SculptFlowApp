using Omni.App.Business.Services.Ai;

namespace Omni.App.Tests.Services;

public class BotRuntimeServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IBotRepository> _bots = new();
    private readonly Mock<IBotTestRepository> _tests = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly List<Bot> _list = [];

    public BotRuntimeServiceTests()
    {
        _bots.Setup(r => r.ListForClientReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _list.ToList());
        _bots.Setup(r => r.ListForClientReadOnlyAsync(It.Is<Guid>(g => g != _clientId), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Plan();
    }

    private BotRuntimeService Sut() => new(_bots.Object, _tests.Object, _entitlements.Object);

    private void Plan(bool ai = true, bool bookings = true, int? maxBots = null)
    {
        var values = new Dictionary<string, string>
        {
            [EntitlementKeys.AiAssistant] = ai ? "true" : "false", [EntitlementKeys.Bookings] = bookings ? "true" : "false",
            [EntitlementKeys.MaxAiBots] = maxBots?.ToString() ?? "unlimited"
        };
        _entitlements.Setup(e => e.GetAsync(_clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientEntitlements(true, true, SubscriptionStatus.Active, "ai-pro", "AI Pro", null, values));
    }

    private Bot Add(string template = BotTemplate.Booking, string status = BotStatus.Active, int minutesOld = 10, params string[] capabilities)
    {
        var bot = new Bot { Id = Guid.NewGuid(), ClientId = _clientId, Name = "Front desk", Template = template, Status = status,
            Instructions = template == BotTemplate.Custom ? "Be brief." : null, Capabilities = capabilities.ToList(),
            HandoffMessage = "Moved to the team.", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesOld) };
        _list.Add(bot);
        return bot;
    }

    [Fact]
    public async Task The_trigger_config_carries_the_bots_composed_instructions_and_drops_booking_actions_without_the_feature()
    {
        var bot = Add(BotTemplate.Booking);
        bot.Language = "fr";
        bot.Description = "WhatsApp front desk";

        var config = (await Sut().ConfigAsync(_clientId, bot.Id))!;
        Assert.Equal((bot.Id, "Front desk", BotTemplate.Booking, "fr"), (config.Id, config.Name, config.Template, config.Language));
        Assert.Equal(BotTemplates.Booking.Capabilities, config.Capabilities);
        Assert.Contains(BotTemplates.Booking.CoreInstructions, config.Instructions);
        Assert.Contains("Always reply in French", config.Instructions);
        Assert.Contains("Your purpose: WhatsApp front desk", config.Instructions);

        Plan(bookings: false);
        var without = (await Sut().ConfigAsync(_clientId, bot.Id))!;
        Assert.Equal([BotCapability.Knowledge, BotCapability.Services], without.Capabilities);
        Assert.Contains("never return action=schedule", without.Instructions);

        Assert.Null(await Sut().ConfigAsync(_clientId, Guid.NewGuid()));
    }

    [Fact]
    public async Task A_live_tool_call_needs_the_clients_own_answering_bot_with_the_capability()
    {
        var bot = Add(BotTemplate.Support);

        var ok = await Sut().AuthorizeAsync(_clientId, bot.Id, null, BotCapability.Knowledge);
        Assert.NotNull(ok.Run);
        Assert.Equal((bot.Id, false, "Moved to the team."), (ok.Run!.BotId, ok.Run.IsTest, ok.Run.HandoffMessage));
        Assert.NotNull((await Sut().AuthorizeAsync(_clientId, bot.Id, null, null)).Run); // reply / handoff: every bot

        Assert.Equal(BotToolCheck.NotAllowed, (await Sut().AuthorizeAsync(_clientId, bot.Id, null, BotCapability.Bookings)).DeniedReason);
        Assert.Equal(BotToolCheck.UnknownBot, (await Sut().AuthorizeAsync(_clientId, null, null, null)).DeniedReason);
        Assert.Equal(BotToolCheck.UnknownBot, (await Sut().AuthorizeAsync(_clientId, Guid.NewGuid(), null, null)).DeniedReason);
        // Another client's id with this bot: refused, so no tool can reach another client's data.
        Assert.Equal(BotToolCheck.UnknownBot, (await Sut().AuthorizeAsync(Guid.NewGuid(), bot.Id, null, null)).DeniedReason);
        Assert.Equal(BotToolCheck.UnknownBot, (await Sut().AuthorizeAsync(Guid.Empty, bot.Id, null, null)).DeniedReason);
    }

    [Fact]
    public async Task A_live_call_from_a_bot_that_no_longer_answers_is_refused()
    {
        var inactive = Add(status: BotStatus.Inactive);
        Assert.Equal(BotToolCheck.BotNotActive, (await Sut().AuthorizeAsync(_clientId, inactive.Id, null, null)).DeniedReason);

        var older = Add(minutesOld: 30);
        var newer = Add(minutesOld: 5);
        Plan(maxBots: 1);
        Assert.NotNull((await Sut().AuthorizeAsync(_clientId, older.Id, null, null)).Run);
        Assert.Equal(BotToolCheck.BotNotActive, (await Sut().AuthorizeAsync(_clientId, newer.Id, null, null)).DeniedReason);

        Plan(ai: false);
        Assert.Equal(BotToolCheck.BotNotActive, (await Sut().AuthorizeAsync(_clientId, older.Id, null, null)).DeniedReason);
    }

    [Fact]
    public async Task A_test_call_may_run_a_draft_but_only_with_its_own_session()
    {
        var draft = Add(BotTemplate.Custom, BotStatus.Draft, 10, BotCapability.Knowledge);
        draft.KnowledgeScope = BotKnowledgeScope.Selected;
        var document = Guid.NewGuid();
        draft.KnowledgeSources.Add(new BotKnowledgeSource { KnowledgeDocumentId = document });
        var session = new BotTestSession { Id = Guid.NewGuid(), ClientId = _clientId, BotId = draft.Id };
        _tests.Setup(t => t.GetSessionAsync(_clientId, session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var run = (await Sut().AuthorizeAsync(_clientId, draft.Id, session.Id, BotCapability.Knowledge)).Run!;
        Assert.Equal((true, session.Id), (run.IsTest, run.TestSessionId!.Value));
        Assert.Equal([document], run.KnowledgeFilter!.DocumentIds);

        Assert.Equal(BotToolCheck.UnknownTestSession, (await Sut().AuthorizeAsync(_clientId, draft.Id, Guid.NewGuid(), null)).DeniedReason);
        var other = Add();
        Assert.Equal(BotToolCheck.UnknownTestSession, (await Sut().AuthorizeAsync(_clientId, other.Id, session.Id, null)).DeniedReason);
        Assert.Equal(BotToolCheck.NotAllowed, (await Sut().AuthorizeAsync(_clientId, draft.Id, session.Id, BotCapability.Bookings)).DeniedReason);
    }

    [Theory]
    [InlineData("language", null)]
    [InlineData("notes", BotCapability.ContactUpdate)]
    [InlineData("followup", BotCapability.ContactUpdate)]
    [InlineData("stage", BotCapability.Pipeline)]
    public void Contact_updates_need_the_capability_of_what_they_change(string field, string? needed)
    {
        var request = new UpdateContactContextRequest(null, "ar", null, null,
            field == "notes" ? "VIP" : null, field == "followup" ? DateTimeOffset.UtcNow : null, null, field == "stage" ? "booked" : null);
        var none = new BotRunContext(_clientId, Guid.NewGuid(), new HashSet<string>(), null, null, null);
        Assert.Equal(needed, Sut().CheckContactUpdate(none, request).MissingCapability);

        var all = none with { Capabilities = BotCapability.All.ToHashSet() };
        Assert.Null(Sut().CheckContactUpdate(all, request).MissingCapability);
    }

    [Fact]
    public void Blank_fields_count_as_not_sent()
    {
        var run = new BotRunContext(_clientId, Guid.NewGuid(), new HashSet<string>(), null, null, null);
        var (clean, missing) = Sut().CheckContactUpdate(run, new UpdateContactContextRequest(null, " ", "", " ", "", null, "", " "));
        Assert.Null(missing);
        Assert.Equal(new UpdateContactContextRequest(null, null, null, null, null, null, null, null), clean);
    }
}
