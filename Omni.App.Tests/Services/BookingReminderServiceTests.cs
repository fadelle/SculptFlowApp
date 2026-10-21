using Microsoft.Extensions.Logging.Abstractions;
using Omni.App.Business.Services.Bookings;

namespace Omni.App.Tests.Services;

/// <summary>Milestone C S5: reminder settings validation, template choice and the switches that stop all sending.</summary>
public class BookingReminderServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IBookingReminderRepository> _reminders = new();
    private readonly Mock<IWhatsAppTemplateRepository> _templates = new();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IConfigManager> _config = new();

    public BookingReminderServiceTests()
    {
        _config.SetupGet(c => c.RemindersEnabled).Returns(true);
        _config.SetupGet(c => c.RemindersBatchSize).Returns(50);
        _entitlements.Setup(e => e.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(ClientEntitlements.Unrestricted);
    }

    private BookingReminderService Sut() => new(_reminders.Object, Mock.Of<IContactRepository>(), Mock.Of<IConversationRepository>(),
        Mock.Of<IConversationService>(), _templates.Object, _clients.Object, _entitlements.Object, Mock.Of<IMessageService>(),
        Mock.Of<IReaderTextFactory>(), _uow.Object, _config.Object, TimeProvider.System, NullLogger<BookingReminderService>.Instance);

    [Fact]
    public async Task Without_settings_reminders_are_off_with_24_and_2_hours()
    {
        var settings = await Sut().GetSettingsAsync(_clientId);
        Assert.Equal((false, 24, 2, 0), (settings.Enabled, settings.FirstHoursBefore, settings.SecondHoursBefore, settings.Templates.Count));
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(24, 169)]
    public async Task Hours_must_be_between_0_and_168(int first, int second)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveSettingsAsync(_clientId, new(true, first, second, new Dictionary<string, Guid?>())));
        Assert.Equal("Hours before must be between 0 and 168.", TextKit.English(error));
    }

    [Fact]
    public async Task Templates_must_be_the_clients_own_and_for_a_dashboard_language()
    {
        var mine = Guid.NewGuid();
        _templates.Setup(t => t.GetReadOnlyAsync(_clientId, mine, It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppTemplate { Id = mine });
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveSettingsAsync(_clientId, new(true, 24, 2, new Dictionary<string, Guid?> { ["en"] = Guid.NewGuid() })));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().SaveSettingsAsync(_clientId, new(true, 24, 2, new Dictionary<string, Guid?> { ["de"] = mine })));

        ReminderSettings? saved = null;
        _reminders.Setup(r => r.AddSettings(It.IsAny<ReminderSettings>())).Callback<ReminderSettings>(s => saved = s);
        await Sut().SaveSettingsAsync(_clientId, new(true, 48, 0, new Dictionary<string, Guid?> { [" AR "] = mine, ["fr"] = null }));

        Assert.Equal((_clientId, true, 48, 0), (saved!.ClientId, saved.IsEnabled, saved.FirstHoursBefore, saved.SecondHoursBefore));
        Assert.Equal($"{{\"ar\":\"{mine}\"}}", saved.TemplatesJson);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void The_template_follows_the_contact_then_the_client_then_any()
    {
        var en = Guid.NewGuid();
        var ar = Guid.NewGuid();
        var templates = new Dictionary<string, Guid> { ["en"] = en, ["ar"] = ar };
        Assert.Equal(ar, BookingReminderService.PickTemplate(templates, "ar", "en"));
        Assert.Equal(en, BookingReminderService.PickTemplate(templates, "es", "en"));
        Assert.Equal(ar, BookingReminderService.PickTemplate(templates, "es", "fr"));
        Assert.Null(BookingReminderService.PickTemplate(new Dictionary<string, Guid>(), "en", "en"));
    }

    [Theory]
    [InlineData("Hi {{1}}, see you {{2}} at {{3}}.", 3)]
    [InlineData("Hi {{1}}, see you on {{2}}.", 2)]
    [InlineData("See you soon!", 0)]
    [InlineData("{{ 4 }} {{1}}", 4)]
    [InlineData(null, 0)]
    public void Placeholders_are_counted_by_the_highest_number(string? body, int expected) =>
        Assert.Equal(expected, BookingReminderService.PlaceholderCount(body));

    [Fact]
    public async Task Nothing_runs_when_the_master_switch_the_clients_switch_or_the_plan_says_no()
    {
        _config.SetupGet(c => c.RemindersEnabled).Returns(false);
        Assert.Empty(await Sut().ListClientsWithRemindersAsync());
        Assert.Equal(0, await Sut().SendDueAsync(_clientId));

        _config.SetupGet(c => c.RemindersEnabled).Returns(true);
        _reminders.Setup(r => r.GetSettingsReadOnlyAsync(_clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReminderSettings { ClientId = _clientId, IsEnabled = false, TemplatesJson = "{}" });
        Assert.Equal(0, await Sut().SendDueAsync(_clientId));

        _reminders.Setup(r => r.GetSettingsReadOnlyAsync(_clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReminderSettings { ClientId = _clientId, IsEnabled = true, FirstHoursBefore = 24, TemplatesJson = "{}" });
        _entitlements.Setup(e => e.GetAsync(_clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClientEntitlements(true, true, SubscriptionStatus.Active, "inbox", "Inbox", null, new Dictionary<string, string>()));
        Assert.Equal(0, await Sut().SendDueAsync(_clientId));
        _reminders.Verify(r => r.ListDueAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
