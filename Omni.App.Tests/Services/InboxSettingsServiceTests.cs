using Omni.App.Business.Services.Inbox;

namespace Omni.App.Tests.Services;

public class InboxSettingsServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IInboxSettingsRepository> _settings = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public InboxSettingsServiceTests()
    {
        _clinics.Setup(r => r.GetReadOnlyAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new Clinic { Id = _clinicId, Name = "Glow" });
        Plan(aiAgent: true);
    }

    private InboxSettingsService Sut() => new(_settings.Object, _clinics.Object, _entitlements.Object, _uow.Object);

    private InboxSettings Row(bool newOn, bool reopenedOn)
    {
        var row = new InboxSettings { Id = Guid.NewGuid(), ClinicId = _clinicId, AiBotNewConversations = newOn, AiBotReopenedConversations = reopenedOn };
        _settings.Setup(r => r.GetForClinicReadOnlyAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        _settings.Setup(r => r.GetForClinicAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        return row;
    }

    private void Plan(bool aiAgent) =>
        _entitlements.Setup(e => e.GetAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new ClinicEntitlements(true, true,
            SubscriptionStatus.Active, "basic", "Basic", null,
            new Dictionary<string, string> { [EntitlementKeys.AiAgent] = aiAgent ? "true" : "false" }));

    [Fact]
    public async Task A_clinic_without_a_row_gets_the_defaults_with_the_bot_on()
    {
        Assert.Equal(new InboxSettingsDto(true, true, true), await Sut().GetAsync(_clinicId));
        Assert.Equal(ConversationMode.Ai, await Sut().InitialModeAsync(_clinicId));
    }

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(true, false, true, false, true)]
    [InlineData(true, true, false, true, false)]
    [InlineData(false, true, true, false, false)]   // a plan without the AI agent overrides the clinic's switches
    public async Task The_plan_and_the_clinic_switches_decide_who_answers(bool plan, bool newOn, bool reopenedOn, bool botNew, bool botReopened)
    {
        Row(newOn, reopenedOn);
        Plan(plan);

        var settings = await Sut().GetAsync(_clinicId);

        Assert.Equal((plan, newOn, reopenedOn, botNew, botReopened),
            (settings.PlanIncludesAiBot, settings.AiBotNewConversations, settings.AiBotReopenedConversations, settings.AiBotAnswersNew, settings.AiBotAnswersReopened));
        Assert.Equal(botNew ? ConversationMode.Ai : ConversationMode.Human, await Sut().InitialModeAsync(_clinicId));
    }

    [Fact]
    public async Task Billing_off_means_the_bot_answers()
    {
        _entitlements.Setup(e => e.GetAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(ClinicEntitlements.Unrestricted);
        Assert.True((await Sut().GetAsync(_clinicId)).AiBotAnswersNew);
    }

    [Fact]
    public async Task An_unknown_clinic_is_not_found()
    {
        var other = Guid.NewGuid();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().GetAsync(other));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().UpdateAsync(other, new UpdateInboxSettingsRequest(true, true)));
        _settings.Verify(r => r.Add(It.IsAny<InboxSettings>()), Times.Never);
    }

    [Fact]
    public async Task The_first_save_creates_the_clinic_wide_row()
    {
        InboxSettings? added = null;
        _settings.Setup(r => r.Add(It.IsAny<InboxSettings>())).Callback<InboxSettings>(s => added = s);

        await Sut().UpdateAsync(_clinicId, new UpdateInboxSettingsRequest(false, true));

        Assert.Equal((_clinicId, (Guid?)null, false, true),
            (added!.ClinicId, added.ChannelIntegrationId, added.AiBotNewConversations, added.AiBotReopenedConversations));
        Assert.NotEqual(Guid.Empty, added.Id);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_later_save_changes_the_row_only_when_something_changed()
    {
        var row = Row(true, true);

        await Sut().UpdateAsync(_clinicId, new UpdateInboxSettingsRequest(true, true));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

        await Sut().UpdateAsync(_clinicId, new UpdateInboxSettingsRequest(true, false));
        Assert.Equal((true, false), (row.AiBotNewConversations, row.AiBotReopenedConversations));
        Assert.True(row.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        _settings.Verify(r => r.Add(It.IsAny<InboxSettings>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
