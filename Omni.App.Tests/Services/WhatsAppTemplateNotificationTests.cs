using Omni.App.Business.Contracts.Providers.WhatsApp;
using Omni.App.Business.Contracts.Services.Notifications;
using Omni.App.Business.Services.WhatsApp;
using Omni.App.Entities.Dtos.Notifications;
using Omni.App.Entities.Dtos.Ui;
using Omni.App.Entities.Requests.WhatsApp;

namespace Omni.App.Tests.Services;

/// <summary>Meta's template events (QA t8-15): the owner is told when a template's status or category changes.</summary>
public class WhatsAppTemplateNotificationTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IWhatsAppTemplateRepository> _templates = new();
    private readonly Mock<IChannelIntegrationRepository> _integrations = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly List<(string Type, NotificationText Text, string? Link)> _sent = [];
    private readonly WhatsAppTemplate _template;

    public WhatsAppTemplateNotificationTests()
    {
        _template = new WhatsAppTemplate
        {
            Id = Guid.NewGuid(), ClientId = _clientId, Name = "spring_promo", Language = "en_US", Category = WhatsAppTemplateCategory.Utility,
            Status = WhatsAppTemplateStatus.Pending, Body = "Hi"
        };
        _templates.Setup(t => t.FindByNameAsync(_clientId, "spring_promo", "en_US", It.IsAny<CancellationToken>())).ReturnsAsync(_template);
        _notifications.Setup(n => n.CreateAsync(_clientId, It.IsAny<string>(), It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, string type, NotificationText text, Guid? _, Guid? _, Guid? _, Guid? _, string? link, CancellationToken _) => _sent.Add((type, text, link)))
            .Returns(Task.CompletedTask);
    }

    private WhatsAppTemplateService Sut() => new(_templates.Object, _integrations.Object, _uow.Object, Mock.Of<IWhatsAppTemplateProvider>(),
        Mock.Of<IInboxNotifier>(), Mock.Of<IEventLogger>(), _notifications.Object, Mock.Of<ICampaignRepository>());

    private Task Event(string? status, string? reason = null, string? currentCategory = null) =>
        Sut().ApplyMetaEventAsync(new WhatsAppTemplateEventRequest(_clientId, "message_template_status_update", null, null, "spring_promo", "en_US",
            null, status, reason, null, null, currentCategory, null, null));

    [Fact]
    public async Task A_rejection_tells_the_owner_why_once_and_links_to_the_templates_page()
    {
        await Event("REJECTED", reason: "INVALID_FORMAT");
        await Event("REJECTED", reason: "INVALID_FORMAT"); // Meta repeats itself: no second notification

        var (type, text, link) = Assert.Single(_sent);
        Assert.Equal((NotificationType.TemplateStatusChanged, "/WhatsApp/Templates"), (type, link));
        Assert.Equal(("notifications.template.rejected.title", "notifications.template.rejected.messageReason"), (text.TitleKey, text.MessageKey));
        Assert.Equal(["spring_promo", "INVALID_FORMAT"], text.Args);
    }

    [Theory]
    [InlineData("APPROVED", "notifications.template.approved.title")]
    [InlineData("PAUSED", "notifications.template.paused.title")]
    [InlineData("DISABLED", "notifications.template.disabled.title")]
    [InlineData("FLAGGED", "notifications.template.flagged.title")]
    public async Task Status_changes_that_affect_sending_are_notified(string status, string titleKey)
    {
        await Event(status);
        Assert.Equal(titleKey, Assert.Single(_sent).Text.TitleKey);
    }

    [Theory]
    [InlineData("PENDING")]
    [InlineData("IN_APPEAL")]
    public async Task Changes_that_dont_affect_sending_are_not_notified(string status)
    {
        _template.Status = WhatsAppTemplateStatus.Approved;
        await Event(status);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task A_new_category_is_notified_with_both_categories_because_it_changes_the_price()
    {
        _template.Status = WhatsAppTemplateStatus.Approved;
        await Event(null, currentCategory: "MARKETING");
        await Event(null, currentCategory: "MARKETING"); // already marketing: nothing new

        var (type, text, _) = Assert.Single(_sent);
        Assert.Equal((NotificationType.TemplateStatusChanged, "notifications.template.category.title"), (type, text.TitleKey));
        Assert.Equal("spring_promo", text.Args[0]);
        Assert.Equal((TextArg.Value("utility"), TextArg.Value("marketing")), (text.Args[1], text.Args[2]));
    }
}
