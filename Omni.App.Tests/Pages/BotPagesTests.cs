using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Pages.Settings;
using static Omni.App.Tests.Pages.PageKit;
using BotsIndex = Omni.App.Pages.Bots.IndexModel;
using BotsEdit = Omni.App.Pages.Bots.EditModel;

namespace Omni.App.Tests.Pages;

public class BotPagesTests
{
    private readonly Client _client = SomeClient();
    private readonly Mock<IBotService> _bots = new();
    private readonly Mock<IConfigManager> _config = new();
    private readonly BotDto _support;

    public BotPagesTests()
    {
        _support = Dto(BotTemplate.Support);
        _bots.Setup(b => b.ListAsync(_client.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => new BotListDto(true, 2, 1, [_support], new HashSet<Guid>()));
        _bots.Setup(b => b.GetAsync(_client.Id, _support.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_support);
        _bots.Setup(b => b.ListKnowledgeChoicesAsync(_client.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _config.SetupGet(c => c.BotsInstructionsMaxLength).Returns(4000);
    }

    private static BotDto Dto(string template, string status = BotStatus.Active, string? instructions = null) => new(Guid.NewGuid(), "Front desk", null, template,
        instructions, BotTemplates.For(template)?.Capabilities ?? [], BotKnowledgeScope.All, [], [], null, BotHandoffPolicy.OnRequestOrUnsure, null, status, 1,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private BotsIndex Index(Client? client = null) => new BotsIndex(ClientContext(client ?? _client).Object, _bots.Object, TextKit.Localizer()).Attach();
    private BotsEdit Edit() => new BotsEdit(ClientContext(_client).Object, _bots.Object, _config.Object, TextKit.Localizer()).Attach();

    [Fact]
    public async Task The_list_loads_the_bots_and_without_a_client_shows_nothing()
    {
        var page = Index();
        await page.OnGetAsync(default);
        Assert.Equal((true, 1), (page.ClientConfigured, page.List!.Bots.Count));

        var none = new BotsIndex(ClientContext(null).Object, _bots.Object, TextKit.Localizer()).Attach();
        await none.OnGetAsync(default);
        Assert.False(none.ClientConfigured);
    }

    [Fact]
    public async Task New_bot_creates_a_draft_named_after_its_template_and_opens_the_editor()
    {
        var created = Dto(BotTemplate.Booking, BotStatus.Draft);
        _bots.Setup(b => b.CreateAsync(_client.Id, BotTemplate.Booking, "Booking bot", It.IsAny<CancellationToken>())).ReturnsAsync(created);
        var redirect = Assert.IsType<RedirectToPageResult>(await Index().OnPostCreateAsync(BotTemplate.Booking, default));
        Assert.Equal(("/Bots/Edit", created.Id), (redirect.PageName, (Guid)redirect.RouteValues!["id"]!));

        _bots.Setup(b => b.CreateAsync(_client.Id, "robot", It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("bots.errors.template"));
        var bad = Index();
        await bad.OnPostCreateAsync("robot", default);
        Assert.Equal("Choose one of the bot types.", bad.ErrorMessage);
    }

    [Fact]
    public async Task Status_changes_report_success_plan_limits_and_missing_bots()
    {
        _bots.Setup(b => b.SetStatusAsync(_client.Id, _support.Id, BotStatus.Active, It.IsAny<CancellationToken>())).ReturnsAsync(_support);
        var page = Index();
        await page.OnPostStatusAsync(_support.Id, BotStatus.Active, default);
        Assert.Equal("Front desk is active.", page.StatusMessage);

        _bots.Setup(b => b.SetStatusAsync(_client.Id, _support.Id, BotStatus.Inactive, It.IsAny<CancellationToken>())).ReturnsAsync(_support with { Status = BotStatus.Inactive });
        await page.OnPostStatusAsync(_support.Id, BotStatus.Inactive, default);
        Assert.Equal("Front desk is no longer active.", page.StatusMessage);

        _bots.Setup(b => b.SetStatusAsync(_client.Id, _support.Id, BotStatus.Active, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bots.errors.instructionsRequired"));
        await page.OnPostStatusAsync(_support.Id, BotStatus.Active, default);
        Assert.Equal("Write the bot's instructions first.", page.ErrorMessage);

        var missing = Guid.NewGuid();
        _bots.Setup(b => b.SetStatusAsync(_client.Id, missing, It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new KeyNotFoundException());
        await page.OnPostStatusAsync(missing, BotStatus.Active, default);
        Assert.Equal("That bot doesn't exist anymore.", page.ErrorMessage);
    }

    [Fact]
    public async Task Use_as_custom_and_delete_act_on_the_clients_bot()
    {
        var copy = Dto(BotTemplate.Custom, BotStatus.Draft, "x");
        _bots.Setup(b => b.DuplicateAsCustomAsync(_client.Id, _support.Id, "Front desk (custom)", It.IsAny<CancellationToken>())).ReturnsAsync(copy);
        var page = Index();
        var redirect = Assert.IsType<RedirectToPageResult>(await page.OnPostDuplicateAsync(_support.Id, default));
        Assert.Equal(copy.Id, (Guid)redirect.RouteValues!["id"]!);
        Assert.StartsWith("Custom copy of Front desk", page.StatusMessage);

        await page.OnPostDeleteAsync(_support.Id, default);
        _bots.Verify(b => b.DeleteAsync(_client.Id, _support.Id, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Front desk deleted.", page.StatusMessage);

        var missing = Index();
        await missing.OnPostDuplicateAsync(Guid.NewGuid(), default);
        await missing.OnPostDeleteAsync(Guid.NewGuid(), default);
        Assert.Equal("That bot doesn't exist anymore.", missing.ErrorMessage);
    }

    [Fact]
    public async Task The_editor_shows_the_saved_bot_and_whether_it_can_be_tested()
    {
        var page = Edit();
        Assert.IsType<PageResult>(await page.OnGetAsync(_support.Id, default));
        Assert.Equal(("Front desk", BotStatus.Active, (string?)null, true), (page.Name, page.Status, page.NotReadyKey, page.PlanIncludesAi));
        Assert.True(page.HasCapability(BotCapability.Services));
        Assert.False(page.HasCapability(BotCapability.Bookings));
        Assert.Equal(4000, page.InstructionsMaxLength);
        Assert.Contains("en", page.Languages);

        Assert.IsType<NotFoundResult>(await Edit().OnGetAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Saving_redirects_with_a_message_and_a_form_error_redisplays_what_was_typed()
    {
        var page = Edit();
        page.Name = "Desk"; page.Language = ""; page.Capabilities = [BotCapability.Knowledge];
        _bots.Setup(b => b.SaveAsync(_client.Id, _support.Id, It.Is<SaveBotRequest>(r => r.Name == "Desk" && r.Language == null), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_support with { Name = "Desk" });
        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(_support.Id, default));
        Assert.Equal("Desk saved.", page.StatusMessage);

        var failing = Edit();
        failing.Name = " ";
        _bots.Setup(b => b.SaveAsync(_client.Id, _support.Id, It.Is<SaveBotRequest>(r => r.Name == " "), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("bots.errors.nameRequired"));
        Assert.IsType<PageResult>(await failing.OnPostAsync(_support.Id, default));
        Assert.Equal(("Give the bot a name.", " "), (failing.ErrorMessage, failing.Name));

        _bots.Setup(b => b.SaveAsync(_client.Id, It.Is<Guid>(g => g != _support.Id), It.IsAny<SaveBotRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new KeyNotFoundException());
        Assert.IsType<NotFoundResult>(await Edit().OnPostAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task The_editor_can_copy_a_template_and_delete_the_bot()
    {
        var copy = Dto(BotTemplate.Custom, BotStatus.Draft, "x");
        _bots.Setup(b => b.DuplicateAsCustomAsync(_client.Id, _support.Id, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(copy);
        var redirect = Assert.IsType<RedirectToPageResult>(await Edit().OnPostDuplicateAsync(_support.Id, default));
        Assert.Equal(copy.Id, (Guid)redirect.RouteValues!["id"]!);
        Assert.Equal("/Bots/Index", Assert.IsType<RedirectToPageResult>(await Edit().OnPostDeleteAsync(_support.Id, default)).PageName);
        Assert.IsType<NotFoundResult>(await Edit().OnPostDeleteAsync(Guid.NewGuid(), default));
        Assert.IsType<NotFoundResult>(await Edit().OnPostDuplicateAsync(Guid.NewGuid(), default));
    }
}

public class InboxSettingsPageTests
{
    private readonly Client _client = SomeClient();
    private readonly Mock<IInboxSettingsService> _inbox = new();
    private readonly Guid _account = Guid.NewGuid();

    public InboxSettingsPageTests() =>
        _inbox.Setup(i => i.GetAsync(_client.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new InboxSettingsDto(true, 2,
            [new AccountAiSettingsDto(_account, ConversationChannel.WhatsApp, "+961 70", false, null, true, true, null, null, AiBotBlockReason.AiDisabled)],
            [new BotChoiceDto(Guid.NewGuid(), "Front desk", BotStatus.Active)]));

    private InboxSettingsModel Sut(Client? client = null) => new InboxSettingsModel(ClientContext(client).Object, _inbox.Object, TextKit.Localizer()).Attach();

    [Fact]
    public async Task Get_lists_the_accounts_and_without_a_client_shows_nothing()
    {
        var page = Sut(_client);
        await page.OnGetAsync(default);
        Assert.Single(page.Settings!.Accounts);
        Assert.Equal("WhatsApp · +961 70", InboxSettingsModel.AccountName(page.Settings.Accounts[0]));
        Assert.Equal("", InboxSettingsModel.AccountName(null));

        var none = Sut();
        await none.OnGetAsync(default);
        Assert.False(none.ClientConfigured);
        Assert.IsType<RedirectToPageResult>(await none.OnPostAsync(default));
    }

    [Fact]
    public async Task Saving_one_account_sends_its_values_and_a_refusal_keeps_what_was_posted()
    {
        var bot = Guid.NewGuid();
        var page = Sut(_client);
        page.ChannelIntegrationId = _account; page.AiEnabled = true; page.BotId = bot; page.AiBotNewConversations = true;
        Assert.IsType<RedirectToPageResult>(await page.OnPostAsync(default));
        _inbox.Verify(i => i.UpdateAccountAsync(_client.Id, _account, new UpdateAccountAiRequest(true, bot, true, false), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("AI settings saved for WhatsApp · +961 70.", page.StatusMessage);

        _inbox.Setup(i => i.UpdateAccountAsync(_client.Id, _account, It.IsAny<UpdateAccountAiRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("inboxSettings.errors.botRequired"));
        var failing = Sut(_client);
        failing.ChannelIntegrationId = _account; failing.AiEnabled = true;
        Assert.IsType<PageResult>(await failing.OnPostAsync(default));
        Assert.Equal("Choose the bot that answers before turning AI on.", failing.ErrorMessage);
        Assert.True(failing.FormValues(failing.Settings!.Accounts[0]).AiEnabled);

        _inbox.Setup(i => i.UpdateAccountAsync(_client.Id, _account, It.IsAny<UpdateAccountAiRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new KeyNotFoundException());
        Assert.IsType<RedirectToPageResult>(await Sut(_client).OnPostAsync(default));
    }
}
