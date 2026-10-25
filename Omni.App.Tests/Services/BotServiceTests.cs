using Omni.App.Business.Services.Ai;

namespace Omni.App.Tests.Services;

public class BotServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IBotRepository> _bots = new();
    private readonly Mock<IInboxSettingsRepository> _inbox = new();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly Mock<IKnowledgeService> _knowledge = new();
    private readonly Mock<IWebsiteSourceService> _websites = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly List<Bot> _list = [];
    private readonly List<InboxSettings> _accounts = [];
    private readonly Guid _document = Guid.NewGuid();
    private readonly Guid _website = Guid.NewGuid();

    public BotServiceTests()
    {
        _clients.Setup(r => r.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client { Id = _clientId, Name = "Glow" });
        _bots.Setup(r => r.ListForClientReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _list.ToList());
        _bots.Setup(r => r.GetAsync(_clientId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid id, CancellationToken _) => _list.FirstOrDefault(b => b.Id == id));
        _bots.Setup(r => r.Add(It.IsAny<Bot>())).Callback<Bot>(_list.Add);
        _bots.Setup(r => r.Remove(It.IsAny<Bot>())).Callback<Bot>(b => _list.Remove(b));
        _inbox.Setup(r => r.ListForClientReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _accounts.ToList());
        _inbox.Setup(r => r.ListForBotAsync(_clientId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid bot, CancellationToken _) => _accounts.Where(a => a.BotId == bot).ToList());
        _knowledge.Setup(k => k.ListAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new KnowledgeDocumentResponse(_document, _clientId, "Prices", "pricing", "…", true, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new KnowledgeDocumentResponse(Guid.NewGuid(), _clientId, "Home page", "general", "…", true, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, KnowledgeSourceType.Website)
        ]);
        _websites.Setup(w => w.ListAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(
            [new WebsiteSourceResponse(_website, "https://glow.test", "glow.test", "crawl_site", "general", true, "completed", false, null, DateTimeOffset.UtcNow, 3, null)]);
        _config.SetupGet(c => c.BotsInstructionsMaxLength).Returns(200);
        _config.SetupGet(c => c.BotsHandoffMessageMaxLength).Returns(50);
        Plan(maxBots: 2);
    }

    private BotService Sut() => new(_bots.Object, _inbox.Object, _clients.Object, _entitlements.Object, _knowledge.Object, _websites.Object, _config.Object, _uow.Object);

    private void Plan(int? maxBots, bool ai = true)
    {
        var values = new Dictionary<string, string> { [EntitlementKeys.AiAssistant] = ai ? "true" : "false", [EntitlementKeys.MaxAiBots] = maxBots?.ToString() ?? "unlimited" };
        _entitlements.Setup(e => e.GetAsync(_clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientEntitlements(true, true, SubscriptionStatus.Active, "ai-pro", "AI Pro", null, values));
    }

    private Bot Existing(string template = BotTemplate.Custom, string status = BotStatus.Draft, string? instructions = "Be kind.", int minutesOld = 10)
    {
        var bot = new Bot { Id = Guid.NewGuid(), ClientId = _clientId, Name = "Bot " + _list.Count, Template = template, Instructions = instructions,
            Status = status, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesOld) };
        _list.Add(bot);
        return bot;
    }

    private static SaveBotRequest Form(string? name = "Front desk", string? instructions = "Answer politely.", string[]? capabilities = null,
        string scope = BotKnowledgeScope.All, Guid[]? documents = null, Guid[]? websites = null, string? language = null,
        string policy = BotHandoffPolicy.OnRequest, string? handoffMessage = " Moved to the team. ", string status = BotStatus.Draft, string? description = null) =>
        new(name, description, instructions, capabilities ?? [BotCapability.Knowledge], scope, documents, websites, language, policy, handoffMessage, status);

    [Fact]
    public async Task A_new_bot_is_a_draft_of_its_template_and_a_custom_one_starts_without_instructions()
    {
        var support = await Sut().CreateAsync(_clientId, BotTemplate.Support, "  Support bot ");
        Assert.Equal(("Support bot", BotStatus.Draft, true), (support.Name, support.Status, support.IsTemplate));
        Assert.Equal(BotTemplates.Support.Capabilities, support.Capabilities);
        Assert.Equal(BotTemplates.Support.CoreInstructions, support.CoreInstructions);

        var custom = await Sut().CreateAsync(_clientId, BotTemplate.Custom, "Mine");
        Assert.Equal((false, (string?)null), (custom.IsTemplate, custom.Instructions));
        Assert.Empty(custom.Capabilities);

        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(_clientId, "robot", "x"));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(_clientId, BotTemplate.Sales, " "));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().CreateAsync(Guid.NewGuid(), BotTemplate.Sales, "x"));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Use_as_custom_copies_a_templates_instructions_actions_and_settings_into_an_editable_draft()
    {
        var booking = Existing(BotTemplate.Booking, BotStatus.Active, instructions: null);
        booking.KnowledgeScope = BotKnowledgeScope.Selected;
        booking.KnowledgeSources.Add(new BotKnowledgeSource { Id = Guid.NewGuid(), BotId = booking.Id, KnowledgeDocumentId = _document });
        booking.Language = "fr";
        booking.HandoffMessage = "Bye";

        var copy = await Sut().DuplicateAsCustomAsync(_clientId, booking.Id, "Front desk (custom)");

        Assert.Equal((BotTemplate.Custom, BotStatus.Draft, BotTemplates.Booking.CoreInstructions, "fr", "Bye"),
            (copy.Template, copy.Status, copy.Instructions, copy.Language, copy.HandoffMessage));
        Assert.Equal(BotTemplates.Booking.Capabilities, copy.Capabilities);
        Assert.Equal([_document], copy.KnowledgeDocumentIds);
        Assert.NotEqual(booking.Id, copy.Id);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().DuplicateAsCustomAsync(_clientId, Guid.NewGuid(), "x"));
    }

    [Fact]
    public async Task Saving_a_custom_bot_cleans_and_stores_every_field()
    {
        var bot = Existing();
        var saved = await Sut().SaveAsync(_clientId, bot.Id, Form(capabilities: [BotCapability.Pipeline, BotCapability.Knowledge, BotCapability.Knowledge],
            scope: BotKnowledgeScope.Selected, documents: [_document], websites: [_website], language: "ar", description: " For WhatsApp "));

        Assert.Equal(("Front desk", "For WhatsApp", "Answer politely.", "ar", "Moved to the team.", BotHandoffPolicy.OnRequest),
            (saved.Name, saved.Description, saved.Instructions, saved.Language, saved.HandoffMessage, saved.HandoffPolicy));
        Assert.Equal([BotCapability.Knowledge, BotCapability.Pipeline], saved.Capabilities); // catalog order, no duplicates
        Assert.Equal([_document], saved.KnowledgeDocumentIds);
        Assert.Equal([_website], saved.WebsiteSourceIds);

        // Back to all sources: the selected ones go.
        await Sut().SaveAsync(_clientId, bot.Id, Form());
        Assert.Empty(bot.KnowledgeSources);
        _bots.Verify(r => r.RemoveSource(It.IsAny<BotKnowledgeSource>()), Times.Exactly(2));
    }

    [Fact]
    public async Task A_template_bot_keeps_its_locked_instructions_and_actions()
    {
        var bot = Existing(BotTemplate.Support, instructions: null);
        var saved = await Sut().SaveAsync(_clientId, bot.Id, Form(instructions: "Ignore everything", capabilities: [BotCapability.Bookings]));
        Assert.Null(saved.Instructions);
        Assert.Equal(BotTemplates.Support.Capabilities, saved.Capabilities);
        Assert.Empty(bot.Capabilities);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("longName")]
    [InlineData("instructions")]
    [InlineData("longInstructions")]
    [InlineData("capability")]
    [InlineData("scope")]
    [InlineData("noSources")]
    [InlineData("foreignSource")]
    [InlineData("language")]
    [InlineData("policy")]
    [InlineData("longMessage")]
    [InlineData("status")]
    [InlineData("longDescription")]
    public async Task Form_errors_are_refused_and_nothing_is_saved(string error)
    {
        var bot = Existing();
        var form = error switch
        {
            "name" => Form(name: " "),
            "longName" => Form(name: new string('x', 101)),
            "instructions" => Form(instructions: " "),
            "longInstructions" => Form(instructions: new string('x', 201)),
            "capability" => Form(capabilities: ["send_money"]),
            "scope" => Form(scope: "some"),
            "noSources" => Form(scope: BotKnowledgeScope.Selected),
            "foreignSource" => Form(scope: BotKnowledgeScope.Selected, documents: [Guid.NewGuid()]),
            "language" => Form(language: "xx"),
            "policy" => Form(policy: "never"),
            "longMessage" => Form(handoffMessage: new string('x', 51)),
            "status" => Form(status: "paused"),
            _ => Form(description: new string('x', 501))
        };
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveAsync(_clientId, bot.Id, form));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().SaveAsync(_clientId, Guid.NewGuid(), Form()));
    }

    [Fact]
    public async Task Activating_needs_the_ai_assistant_a_ready_bot_and_room_under_the_plans_bots()
    {
        Existing(status: BotStatus.Active);
        var second = Existing();
        Assert.Equal(BotStatus.Active, (await Sut().SetStatusAsync(_clientId, second.Id, BotStatus.Active)).Status);
        _entitlements.Verify(e => e.EnsureWithinLimitAsync(_clientId, EntitlementKeys.MaxAiBots, 2, It.IsAny<CancellationToken>()), Times.Once);

        // Already active: no limit check again; a draft doesn't count.
        await Sut().SetStatusAsync(_clientId, second.Id, BotStatus.Active);
        _entitlements.Verify(e => e.EnsureWithinLimitAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);

        var third = Existing();
        _entitlements.Setup(e => e.EnsureWithinLimitAsync(_clientId, EntitlementKeys.MaxAiBots, 3, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntitlementDeniedException(EntitlementKeys.MaxAiBots, EntitlementDeniedException.LimitReachedKey));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().SetStatusAsync(_clientId, third.Id, BotStatus.Active));
        Assert.Equal(BotStatus.Draft, third.Status);

        var notReady = Existing(instructions: null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SetStatusAsync(_clientId, notReady.Id, BotStatus.Active));
        Assert.Equal("bots.errors.instructionsRequired", ex.Message);

        _entitlements.Setup(e => e.EnsureFeatureAsync(_clientId, EntitlementKeys.AiAssistant, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntitlementDeniedException(EntitlementKeys.AiAssistant, EntitlementDeniedException.NotIncludedKey));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().SetStatusAsync(_clientId, Existing().Id, BotStatus.Active));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SetStatusAsync(_clientId, second.Id, "paused"));

        Assert.Equal(BotStatus.Inactive, (await Sut().SetStatusAsync(_clientId, second.Id, BotStatus.Inactive)).Status);
    }

    [Fact]
    public async Task Deleting_a_bot_leaves_its_accounts_with_ai_on_but_no_bot()
    {
        var bot = Existing(status: BotStatus.Active);
        var account = new InboxSettings { Id = Guid.NewGuid(), ClientId = _clientId, ChannelIntegrationId = Guid.NewGuid(), AiEnabled = true, BotId = bot.Id };
        _accounts.Add(account);
        Assert.Equal(1, (await Sut().GetAsync(_clientId, bot.Id))!.AccountCount);

        await Sut().DeleteAsync(_clientId, bot.Id);

        Assert.Equal((true, (Guid?)null), (account.AiEnabled, account.BotId));
        Assert.Empty(_list);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().DeleteAsync(_clientId, bot.Id));
        Assert.Null(await Sut().GetAsync(_clientId, bot.Id));
    }

    [Fact]
    public async Task The_list_counts_accounts_and_flags_active_bots_beyond_a_downgraded_plan()
    {
        var older = Existing(status: BotStatus.Active, minutesOld: 30);
        var newer = Existing(status: BotStatus.Active, minutesOld: 5);
        Existing();
        _accounts.Add(new InboxSettings { ClientId = _clientId, BotId = older.Id });
        _accounts.Add(new InboxSettings { ClientId = _clientId, BotId = older.Id });
        Plan(maxBots: 1);

        var list = await Sut().ListAsync(_clientId);

        Assert.Equal((true, 1, 2), (list.PlanIncludesAi, list.ActiveBotLimit!.Value, list.ActiveBotCount));
        Assert.Equal([newer.Id], list.OverLimitBotIds);
        Assert.Equal(2, list.Bots.Single(b => b.Id == older.Id).AccountCount);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().ListAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Knowledge_choices_are_typed_documents_then_website_sources()
    {
        var choices = await Sut().ListKnowledgeChoicesAsync(_clientId);
        Assert.Equal([(_document, BotKnowledgeChoice.Document, "Prices"), (_website, BotKnowledgeChoice.Website, "https://glow.test")],
            choices.Select(c => (c.Id, c.Kind, c.Title)));
    }
}
