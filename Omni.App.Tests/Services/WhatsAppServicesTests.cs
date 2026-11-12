using Omni.App.Business.Services.WhatsApp;

namespace Omni.App.Tests.Services;

public class WhatsAppTemplateServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IWhatsAppTemplateRepository> _templates = new();
    private readonly Mock<IChannelIntegrationRepository> _integrations = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IWhatsAppTemplateProvider> _provider = new();
    private readonly Mock<IInboxNotifier> _notifier = new();
    private readonly Mock<IEventLogger> _events = new();
    private readonly Mock<ICampaignRepository> _campaigns = new();
    private ChannelIntegration? _integration;

    public WhatsAppTemplateServiceTests()
    {
        _provider.Setup(p => p.IsReady(It.IsAny<ChannelIntegration>())).Returns(true);
        _integrations.Setup(i => i.GetAsync(_clientId, ChannelType.WhatsApp, It.IsAny<CancellationToken>())).ReturnsAsync(() => _integration);
        Connect();
    }

    private void Connect() => _integration = new ChannelIntegration
    {
        Id = Guid.NewGuid(), ClientId = _clientId, Channel = ChannelType.WhatsApp, Status = ChannelIntegrationStatus.Connected, WhatsAppBusinessId = "W1"
    };

    private WhatsAppTemplateService Sut() => new(_templates.Object, _integrations.Object, _uow.Object, _provider.Object, _notifier.Object, _events.Object, Mock.Of<INotificationService>(), _campaigns.Object);

    // ---------------------------------------------------------------- QA t8-17: edit and delete

    private WhatsAppTemplate Stored(string status, string? metaId = "meta-7")
    {
        var t = new WhatsAppTemplate { Id = Guid.NewGuid(), ClientId = _clientId, Name = "spring", Language = "en_US", Category = "marketing", Body = "Old", Status = status, MetaTemplateId = metaId, RejectionReason = "INVALID_FORMAT" };
        _templates.Setup(r => r.GetAsync(_clientId, t.Id, It.IsAny<CancellationToken>())).ReturnsAsync(t);
        return t;
    }

    private static UpdateWhatsAppTemplateRequest Edit(string body = "Hi {{1}}, new text", string category = "utility") => new(category, body, "text", "Header", " ");

    [Theory]
    [InlineData(WhatsAppTemplateStatus.Rejected)]
    [InlineData(WhatsAppTemplateStatus.Paused)]
    public async Task Editing_a_template_meta_has_sends_the_edit_and_puts_it_back_in_review(string status)
    {
        var t = Stored(status);
        var r = await Sut().UpdateAsync(_clientId, t.Id, Edit());
        _provider.Verify(p => p.EditAsync(_integration!, t, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal((WhatsAppTemplateStatus.Pending, (string?)null, "Hi {{1}}, new text", "utility", "text", "Header", (string?)null),
            (r!.Status, t.RejectionReason, t.Body, t.Category, t.HeaderType, t.HeaderContent, t.Footer));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_refused_edit_changes_nothing()
    {
        var t = Stored(WhatsAppTemplateStatus.Rejected);
        _provider.Setup(p => p.EditAsync(It.IsAny<ChannelIntegration>(), t, It.IsAny<CancellationToken>())).ThrowsAsync(new WhatsAppTemplateProviderException("(#100) Invalid parameter"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().UpdateAsync(_clientId, t.Id, Edit()));
        Assert.Equal("templates.errors.editRefused", ex.Message);
        _uow.Verify(u => u.DiscardChanges(), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Editing_one_meta_never_accepted_submits_it_again_or_keeps_a_draft_without_whatsapp()
    {
        var failed = Stored(WhatsAppTemplateStatus.Rejected, metaId: null);
        _provider.Setup(p => p.SubmitAsync(It.IsAny<ChannelIntegration>(), failed, It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppTemplateSubmitResult("meta-9", "PENDING"));
        Assert.Equal(WhatsAppTemplateStatus.Pending, (await Sut().UpdateAsync(_clientId, failed.Id, Edit()))!.Status);
        Assert.Equal("meta-9", failed.MetaTemplateId);
        _provider.Verify(p => p.EditAsync(It.IsAny<ChannelIntegration>(), It.IsAny<WhatsAppTemplate>(), It.IsAny<CancellationToken>()), Times.Never);

        _integration = null;
        var draft = Stored(WhatsAppTemplateStatus.Rejected, metaId: null);
        var r = await Sut().UpdateAsync(_clientId, draft.Id, Edit());
        Assert.Equal((WhatsAppTemplateStatus.Draft, (string?)null), (r!.Status, draft.RejectionReason));
    }

    [Theory]
    [InlineData(WhatsAppTemplateStatus.Approved)]
    [InlineData(WhatsAppTemplateStatus.Pending)]
    [InlineData(WhatsAppTemplateStatus.Disabled)]
    public async Task Only_draft_rejected_or_paused_templates_can_be_edited(string status)
    {
        var t = Stored(status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().UpdateAsync(_clientId, t.Id, Edit()));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAsync(_clientId, Stored(WhatsAppTemplateStatus.Rejected).Id, Edit(body: " ")));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAsync(_clientId, Stored(WhatsAppTemplateStatus.Rejected).Id, Edit(category: "bogus")));
        Assert.Null(await Sut().UpdateAsync(_clientId, Guid.NewGuid(), Edit()));
    }

    [Fact]
    public async Task Deleting_removes_it_at_meta_and_here_so_the_name_is_free_again()
    {
        var atMeta = Stored(WhatsAppTemplateStatus.Rejected);
        Assert.True(await Sut().DeleteAsync(_clientId, atMeta.Id));
        _provider.Verify(p => p.DeleteAsync(_integration!, atMeta, It.IsAny<CancellationToken>()), Times.Once);
        _templates.Verify(r => r.Remove(atMeta), Times.Once);

        var local = Stored(WhatsAppTemplateStatus.Draft, metaId: null);
        _integration = null; // a draft Meta never had needs no connection
        Assert.True(await Sut().DeleteAsync(_clientId, local.Id));
        _provider.Verify(p => p.DeleteAsync(It.IsAny<ChannelIntegration>(), local, It.IsAny<CancellationToken>()), Times.Never);
        _templates.Verify(r => r.Remove(local), Times.Once);
        Assert.False(await Sut().DeleteAsync(_clientId, Guid.NewGuid()));
    }

    [Fact]
    public async Task A_template_a_campaign_uses_or_meta_refuses_to_delete_is_kept()
    {
        var used = Stored(WhatsAppTemplateStatus.Approved);
        _campaigns.Setup(c => c.FindNameUsingTemplateAsync(_clientId, used.Id, It.IsAny<CancellationToken>())).ReturnsAsync("Spring sale");
        var inUse = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().DeleteAsync(_clientId, used.Id));
        Assert.Equal("templates.errors.usedByCampaign", inUse.Message);

        var refused = Stored(WhatsAppTemplateStatus.Paused);
        _provider.Setup(p => p.DeleteAsync(It.IsAny<ChannelIntegration>(), refused, It.IsAny<CancellationToken>())).ThrowsAsync(new WhatsAppTemplateProviderException("(#100) nope"));
        Assert.Equal("templates.errors.deleteRefused", (await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().DeleteAsync(_clientId, refused.Id))).Message);

        _integration = null;
        var offline = Stored(WhatsAppTemplateStatus.Rejected);
        Assert.Equal("templates.errors.connectFirst", (await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().DeleteAsync(_clientId, offline.Id))).Message);

        _templates.Verify(r => r.Remove(It.IsAny<WhatsAppTemplate>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private CreateWhatsAppTemplateRequest Create(string category = "marketing", string name = "promo", string body = "Hi", string language = "") =>
        new(_clientId, name, category, language, body, null, null, null, null, null);

    [Theory]
    [InlineData("bogus", "n", "b")]
    [InlineData("marketing", " ", "b")]
    [InlineData("marketing", "n", " ")]
    [InlineData("marketing", "Bad Name!", "b")]
    [InlineData("marketing", "Promo", "b")]
    [InlineData("marketing", "spring sale", "b")]
    [InlineData("marketing", "promo-2", "b")]
    public async Task Create_validates(string category, string name, string body) =>
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(Create(category, name, body)));

    // QA t8-13: each of these was a 500 (one after saving an orphan draft); now a 400 naming the field, and nothing is saved.
    public static TheoryData<CreateWhatsAppTemplateRequest, string> BadTemplates()
    {
        var ok = new CreateWhatsAppTemplateRequest(Guid.Empty, "promo", "marketing", "en_US", "Hi", null, null, null, null, null);
        return new()
        {
            { ok with { Name = new string('a', 201) }, "templates.errors.nameInvalid" },
            { ok with { Language = "klingon<script>" }, WhatsAppTemplateService.LanguageInvalidKey },
            { ok with { Language = "english" }, WhatsAppTemplateService.LanguageInvalidKey },
            { ok with { HeaderType = "hologram" }, WhatsAppTemplateService.HeaderTypeInvalidKey },
            { ok with { HeaderType = "text", HeaderContent = new string('h', 61) }, RequestChecks.TooLongKey },
            { ok with { Body = new string('b', 1025) }, RequestChecks.TooLongKey },
            { ok with { Footer = new string('f', 61) }, RequestChecks.TooLongKey },
            { ok with { ButtonsJson = "{not json" }, WhatsAppTemplateService.ButtonsInvalidKey },
            { ok with { ButtonsJson = "{\"a\":1}" }, WhatsAppTemplateService.ButtonsInvalidKey },
            { ok with { ButtonsJson = "[]" }, WhatsAppTemplateService.ButtonsInvalidKey },
            { ok with { ButtonsJson = "[{\"text\":\"Yes\"}]" }, WhatsAppTemplateService.ButtonsInvalidKey },
            { ok with { ButtonsJson = "[{\"type\":\"QUICK_REPLY\",\"text\":\"" + new string('x', 26) + "\"}]" }, WhatsAppTemplateService.ButtonsInvalidKey },
            { ok with { VariablesJson = "{nope" }, RequestChecks.NotJsonKey },
        };
    }

    [Theory]
    [MemberData(nameof(BadTemplates))]
    public async Task Create_refuses_bad_values_before_saving_anything(CreateWhatsAppTemplateRequest request, string key)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(request with { ClientId = _clientId }));
        Assert.Equal(key, ex.Message);
        _templates.Verify(t => t.Add(It.IsAny<WhatsAppTemplate>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_accepts_a_200_character_name_meta_language_codes_and_meta_button_shapes()
    {
        _provider.Setup(p => p.SubmitAsync(_integration!, It.IsAny<WhatsAppTemplate>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppTemplateSubmitResult("meta-1", "PENDING"));
        var buttons = "[{\"type\":\"QUICK_REPLY\",\"text\":\"Yes\"},{\"type\":\"URL\",\"text\":\"Open\",\"url\":\"https://x.test\"},{\"type\":\"COPY_CODE\",\"example\":\"SAVE10\"}]";
        foreach (var language in new[] { "en", "fil", "pt_BR" })
        {
            await Sut().CreateAsync(Create(name: new string('a', 200), language: language) with { ButtonsJson = buttons, HeaderType = "none" });
        }
        _templates.Verify(t => t.Add(It.IsAny<WhatsAppTemplate>()), Times.Exactly(3));
    }

    // QA t3-inbox-19: Meta's own upper-case category was refused; it's stored lower-case.
    [Fact]
    public async Task Create_and_edit_accept_metas_upper_case_category()
    {
        _provider.Setup(p => p.SubmitAsync(_integration!, It.IsAny<WhatsAppTemplate>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppTemplateSubmitResult("meta-1", "PENDING"));
        WhatsAppTemplate? added = null;
        _templates.Setup(t => t.Add(It.IsAny<WhatsAppTemplate>())).Callback<WhatsAppTemplate>(t => added = t);
        await Sut().CreateAsync(Create() with { Category = " UTILITY " });
        Assert.Equal(WhatsAppTemplateCategory.Utility, added!.Category);

        var stored = Stored(WhatsAppTemplateStatus.Draft, metaId: null);
        await Sut().UpdateAsync(_clientId, stored.Id, Edit() with { Category = "MARKETING" });
        Assert.Equal(WhatsAppTemplateCategory.Marketing, stored.Category);
    }

    [Fact]
    public async Task Editing_checks_the_header_type_and_lengths_too()
    {
        var t = Stored(WhatsAppTemplateStatus.Draft, metaId: null);
        Assert.Equal(WhatsAppTemplateService.HeaderTypeInvalidKey,
            (await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAsync(_clientId, t.Id, Edit() with { HeaderType = "hologram" }))).Message);
        Assert.Equal(RequestChecks.TooLongKey,
            (await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAsync(_clientId, t.Id, Edit(body: new string('b', 1025))))).Message);
    }

    [Fact]
    public async Task Create_submits_to_the_provider_and_maps_the_status()
    {
        WhatsAppTemplate? added = null;
        _templates.Setup(t => t.Add(It.IsAny<WhatsAppTemplate>())).Callback<WhatsAppTemplate>(t => added = t);
        _provider.Setup(p => p.SubmitAsync(_integration!, It.IsAny<WhatsAppTemplate>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppTemplateSubmitResult("meta-1", "PENDING"));
        var r = await Sut().CreateAsync(Create());
        Assert.Equal(("en_US", "meta-1", WhatsAppTemplateStatus.Pending), (added!.Language, added.MetaTemplateId, added.Status));
        Assert.Equal(WhatsAppTemplateStatus.Pending, r.Status);
    }

    [Fact]
    public async Task Create_stays_draft_when_nothing_is_connected()
    {
        _integration = null;
        var draft = await Sut().CreateAsync(Create(name: "other"));
        Assert.Equal(WhatsAppTemplateStatus.Draft, draft.Status);
    }

    [Fact]
    public async Task A_provider_rejection_is_stored_not_thrown()
    {
        _provider.Setup(p => p.SubmitAsync(It.IsAny<ChannelIntegration>(), It.IsAny<WhatsAppTemplate>(), It.IsAny<CancellationToken>())).ThrowsAsync(new WhatsAppTemplateProviderException("Body too long"));
        var r = await Sut().CreateAsync(Create());
        Assert.Equal((WhatsAppTemplateStatus.Rejected, "Body too long", (string?)null), (r.Status, r.RejectionReason, r.SubmitError));
    }

    [Fact]
    public async Task A_template_that_couldnt_reach_meta_stays_a_draft_and_says_why_instead_of_rejected()
    {
        // QA t8-12: an invalid token marked new templates "Rejected" with Meta's raw JSON as the reason.
        _provider.Setup(p => p.SubmitAsync(It.IsAny<ChannelIntegration>(), It.IsAny<WhatsAppTemplate>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WhatsAppTemplateProviderException("Invalid OAuth access token - Cannot parse access token", couldNotReach: true));

        var created = await Sut().CreateAsync(Create());
        Assert.Equal((WhatsAppTemplateStatus.Draft, (string?)null, "Invalid OAuth access token - Cannot parse access token"),
            (created.Status, created.RejectionReason, created.SubmitError));

        var draft = Existing();
        var retried = await Sut().RetrySubmitAsync(_clientId, draft.Id);
        Assert.Equal((WhatsAppTemplateStatus.Draft, (string?)null), (draft.Status, draft.RejectionReason));
        Assert.NotNull(retried!.SubmitError);
    }

    private WhatsAppTemplate Existing(string? metaId = null, string status = WhatsAppTemplateStatus.Draft)
    {
        var t = new WhatsAppTemplate { Id = Guid.NewGuid(), ClientId = _clientId, Name = "promo", Body = "b", MetaTemplateId = metaId, Status = status };
        _templates.Setup(r => r.GetAsync(_clientId, t.Id, It.IsAny<CancellationToken>())).ReturnsAsync(t);
        _templates.Setup(r => r.GetReadOnlyAsync(_clientId, t.Id, It.IsAny<CancellationToken>())).ReturnsAsync(t);
        return t;
    }

    [Fact]
    public async Task Retry_submit_rules()
    {
        Assert.Null(await Sut().RetrySubmitAsync(_clientId, Guid.NewGuid()));
        var sent = Existing("meta-1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().RetrySubmitAsync(_clientId, sent.Id));

        var draft = Existing();
        _integration = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().RetrySubmitAsync(_clientId, draft.Id));

        Connect();
        _provider.Setup(p => p.SubmitAsync(It.IsAny<ChannelIntegration>(), draft, It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppTemplateSubmitResult("m2", "APPROVED"));
        var r = await Sut().RetrySubmitAsync(_clientId, draft.Id);
        Assert.Equal(WhatsAppTemplateStatus.Approved, r!.Status);
    }

    [Fact]
    public async Task Sync_refreshes_the_status_and_keeps_or_clears_the_reason()
    {
        Assert.Null(await Sut().SyncStatusAsync(_clientId, Guid.NewGuid()));
        var unsent = Existing();
        Assert.Equal(WhatsAppTemplateStatus.Draft, (await Sut().SyncStatusAsync(_clientId, unsent.Id))!.Status);
        _provider.Verify(p => p.GetStatusAsync(It.IsAny<ChannelIntegration>(), It.IsAny<WhatsAppTemplate>(), It.IsAny<CancellationToken>()), Times.Never);

        var t = Existing("meta-1", status: WhatsAppTemplateStatus.Pending);
        _provider.Setup(p => p.GetStatusAsync(It.IsAny<ChannelIntegration>(), t, It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppTemplateStatusResult("REJECTED", "ABUSIVE"));
        var rejected = await Sut().SyncStatusAsync(_clientId, t.Id);
        Assert.Equal((WhatsAppTemplateStatus.Rejected, "ABUSIVE"), (rejected!.Status, rejected.RejectionReason));

        _provider.Setup(p => p.GetStatusAsync(It.IsAny<ChannelIntegration>(), t, It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppTemplateStatusResult("APPROVED", null));
        var approved = await Sut().SyncStatusAsync(_clientId, t.Id);
        Assert.Null(approved!.RejectionReason);
    }

    [Fact]
    public async Task Sync_refuses_without_a_connection()
    {
        var mine = Existing("meta-2");
        _integration = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SyncStatusAsync(_clientId, mine.Id));
    }

    [Fact]
    public async Task List_and_GetById_map_templates()
    {
        var t = Existing();
        _templates.Setup(r => r.ListForClientReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WhatsAppTemplate> { t });
        Assert.Single(await Sut().ListAsync(_clientId));
        Assert.Equal(t.Id, (await Sut().GetByIdAsync(_clientId, t.Id))!.Id);
        Assert.Null(await Sut().GetByIdAsync(_clientId, Guid.NewGuid()));
    }

    private WhatsAppTemplateEventRequest Event(string? waba = "W1", string? metaId = "meta-1", string? status = "APPROVED", string? category = null) =>
        new(_clientId, "template_status_update", waba, metaId, "promo", null, category, status, null, null, null, null, null, null);

    [Fact]
    public async Task Meta_event_updates_an_existing_template_by_provider_id_and_notifies()
    {
        var t = Existing("meta-1", status: WhatsAppTemplateStatus.Pending);
        _templates.Setup(r => r.FindByProviderTemplateIdAsync(_clientId, "meta-1", It.IsAny<CancellationToken>())).ReturnsAsync(t);
        t.RejectionReason = "INVALID_FORMAT";
        var r = await Sut().ApplyMetaEventAsync(Event() with { Reason = "NONE", QualityRating = "GREEN", CurrentCategory = "utility", Category = "UTILITY" });
        // Approved has no reason: Meta's "NONE" isn't one and an old rejection reason goes (QA t10-06).
        Assert.Equal((WhatsAppTemplateStatus.Approved, (string?)null, "GREEN", "utility", "utility"), (t.Status, t.RejectionReason, t.QualityRating, t.CurrentCategory, t.Category));
        Assert.Equal(_integration!.Id, t.ChannelIntegrationId);
        _notifier.Verify(n => n.WhatsAppTemplateUpdatedAsync(_clientId, t.Id, "promo", WhatsAppTemplateStatus.Approved, null, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _events.Verify(e => e.Log(_clientId, EventTypes.WhatsAppTemplateStatusChanged, null, null, null, "n8n", It.Is<string>(s => s.Contains("\"previousStatus\":\"pending\""))), Times.Once);
        Assert.Equal(t.Id, r.Id);
    }

    [Fact]
    public async Task Meta_event_falls_back_to_name_and_language_then_creates_an_unknown_template()
    {
        var byName = Existing();
        _templates.Setup(r => r.FindByNameAsync(_clientId, "promo", "en_US", It.IsAny<CancellationToken>())).ReturnsAsync(byName);
        await Sut().ApplyMetaEventAsync(Event(metaId: null, status: "PAUSED"));
        Assert.Equal(WhatsAppTemplateStatus.Paused, byName.Status);

        WhatsAppTemplate? added = null;
        _templates.Setup(r => r.Add(It.IsAny<WhatsAppTemplate>())).Callback<WhatsAppTemplate>(t => added = t);
        _templates.Setup(r => r.FindByNameAsync(_clientId, "promo", "en_US", It.IsAny<CancellationToken>())).ReturnsAsync((WhatsAppTemplate?)null);
        await Sut().ApplyMetaEventAsync(Event(status: "WEIRD", category: "MARKETING"));
        Assert.Equal(("meta-1", "weird", "marketing", ""), (added!.MetaTemplateId, added.Status, added.Category, added.Body));
    }

    [Fact]
    public async Task A_template_event_with_values_that_dont_fit_is_a_400_and_nothing_is_stored()
    {
        // QA t8-06: a 40-character status was a 500 with an HTML page.
        var tooLong = await Assert.ThrowsAsync<ArgumentException>(() => Sut().ApplyMetaEventAsync(Event(status: new string('X', 40))));
        Assert.Equal(RequestChecks.TooLongKey, tooLong.Message);
        var notJson = await Assert.ThrowsAsync<ArgumentException>(() => Sut().ApplyMetaEventAsync(Event() with { ComponentsJson = "{bad" }));
        Assert.Equal(RequestChecks.NotJsonKey, notJson.Message);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- QA t10-06 ----

    [Theory]
    [InlineData("REINSTATED")]
    [InlineData("UNARCHIVED")]
    public async Task A_reinstated_template_is_approved_again(string status)
    {
        var t = Existing("meta-1", status: WhatsAppTemplateStatus.Paused);
        t.RejectionReason = "FIRST_PAUSE Paused for low quality.";
        _templates.Setup(r => r.FindByProviderTemplateIdAsync(_clientId, "meta-1", It.IsAny<CancellationToken>())).ReturnsAsync(t);
        await Sut().ApplyMetaEventAsync(Event(status: status) with { Reason = "NONE" });
        Assert.Equal((WhatsAppTemplateStatus.Approved, (string?)null), (t.Status, t.RejectionReason));
    }

    [Fact]
    public async Task A_rejection_keeps_the_code_and_Metas_explanation_and_a_new_status_clears_an_old_reason()
    {
        var t = Existing("meta-1", status: WhatsAppTemplateStatus.Pending);
        _templates.Setup(r => r.FindByProviderTemplateIdAsync(_clientId, "meta-1", It.IsAny<CancellationToken>())).ReturnsAsync(t);
        await Sut().ApplyMetaEventAsync(Event(status: "REJECTED") with { Reason = "INVALID_FORMAT", ReasonDetail = "Variables can't be at the start." });
        Assert.Equal((WhatsAppTemplateStatus.Rejected, "INVALID_FORMAT\nVariables can't be at the start."), (t.Status, t.RejectionReason));

        await Sut().ApplyMetaEventAsync(Event(status: "REJECTED")); // repeated without a reason: kept
        Assert.Equal("INVALID_FORMAT\nVariables can't be at the start.", t.RejectionReason);

        await Sut().ApplyMetaEventAsync(Event(status: "ARCHIVED") with { Reason = "NONE" }); // another status, no reason: cleared
        Assert.Equal(("archived", (string?)null), (t.Status, t.RejectionReason));
    }

    [Theory]
    [InlineData("message_template_quality_update", null)]
    [InlineData("template_category_update", null)]
    [InlineData("message_template_quality_update", "x")]
    public async Task A_quality_or_category_event_for_an_unknown_template_makes_no_row(string eventType, string? status)
    {
        var r = await Sut().ApplyMetaEventAsync(new WhatsAppTemplateEventRequest(_clientId, eventType, "W1", "meta-9", "qa_t10_quality", "en_US",
            null, status, null, "RED", null, null, null, null));
        Assert.Null(r);
        _templates.Verify(x => x.Add(It.IsAny<WhatsAppTemplate>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Meta_event_validates_name_and_waba_ownership()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().ApplyMetaEventAsync(Event() with { Name = " " }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ApplyMetaEventAsync(Event(waba: "OTHER")));
        await Sut().ApplyMetaEventAsync(Event(waba: "w1")); // case-insensitive match is fine
    }

    [Theory]
    [InlineData("pending", WhatsAppTemplateStatus.Pending)]
    [InlineData("APPROVED", WhatsAppTemplateStatus.Approved)]
    [InlineData("Rejected", WhatsAppTemplateStatus.Rejected)]
    [InlineData("PAUSED", WhatsAppTemplateStatus.Paused)]
    [InlineData("DISABLED", WhatsAppTemplateStatus.Disabled)]
    [InlineData("FLAGGED", WhatsAppTemplateStatus.Flagged)]
    [InlineData("DELETED", WhatsAppTemplateStatus.Deleted)]
    [InlineData("IN_APPEAL", "in_appeal")]
    public void Status_mapping(string meta, string expected) => Assert.Equal(expected, WhatsAppTemplateService.MapMetaStatus(meta));

}

public class WhatsAppHealthServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IChannelIntegrationRepository> _integrations = new();
    private readonly Mock<IWhatsAppHealthEventRepository> _healthEvents = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IInboxNotifier> _notifier = new();
    private readonly Mock<IEventLogger> _events = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly ChannelIntegration _integration;

    public WhatsAppHealthServiceTests()
    {
        _integration = new ChannelIntegration
        {
            Id = Guid.NewGuid(), ClientId = _clientId, Channel = ChannelType.WhatsApp, Status = ChannelIntegrationStatus.Connected,
            WhatsAppBusinessId = "W1", PhoneNumberId = "P1", HealthLevel = WhatsAppHealthLevel.Healthy
        };
        _integrations.Setup(i => i.GetAsync(_clientId, ChannelType.WhatsApp, It.IsAny<CancellationToken>())).ReturnsAsync(_integration);
    }

    private WhatsAppHealthService Sut() => new(_integrations.Object, _healthEvents.Object, _uow.Object, _notifier.Object, _events.Object, _notifications.Object);

    private WhatsAppHealthEventRequest Req(string type, string? status = null, string? quality = null, string? waba = "W1", string? phone = "P1", string? code = null, string? message = null, DateTimeOffset? at = null) =>
        new(_clientId, type, waba, phone, status, quality, code, message, at, "{}");

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task A_broken_connection_found_by_a_send_marks_health_and_notifies_once(bool moved, int notices)
    {
        // QA t8-10.
        var failure = new WhatsAppSendException("WhatsApp send failed (401): Bad token (code 190)", 401, 190, "Error validating access token");
        _integrations.Setup(i => i.MarkSendProblemAsync(_integration, "token_invalid", "Error validating access token", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(moved);

        await Sut().RecordSendFailureAsync(_integration, failure);

        _notifier.Verify(n => n.WhatsAppHealthUpdatedAsync(_clientId, _integration.Id, WhatsAppHealthLevel.Problem, It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<string?>(), "Error validating access token", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.IntegrationUnhealthy, It.IsAny<NotificationText>(), null, null, null, _integration.Id,
            "/WhatsApp/Health", It.IsAny<CancellationToken>()), Times.Exactly(notices));
    }

    [Fact]
    public async Task A_send_error_that_isnt_the_connection_changes_no_health()
    {
        await Sut().RecordSendFailureAsync(_integration, new WhatsAppSendException("paused", 400, 132015, "Template paused"));
        _integrations.Verify(i => i.MarkSendProblemAsync(It.IsAny<ChannelIntegration>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_health_event_with_bad_raw_metadata_or_an_oversized_status_is_a_400()
    {
        // QA t8-06: "{bad" raw metadata was a 500.
        var notJson = await Assert.ThrowsAsync<ArgumentException>(() => Sut().ApplyHealthEventAsync(Req("PHONE_NUMBER_QUALITY_UPDATE") with { RawMetadataJson = "{bad" }));
        Assert.Equal(RequestChecks.NotJsonKey, notJson.Message);
        var tooLong = await Assert.ThrowsAsync<ArgumentException>(() => Sut().ApplyHealthEventAsync(Req("PHONE_NUMBER_QUALITY_UPDATE", status: new string('X', 51))));
        Assert.Equal(RequestChecks.TooLongKey, tooLong.Message);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Health_is_null_without_a_connection_and_unknown_until_an_event_arrives()
    {
        Assert.Null(await new WhatsAppHealthService(Mock.Of<IChannelIntegrationRepository>(), _healthEvents.Object, _uow.Object, _notifier.Object, _events.Object, _notifications.Object).GetHealthAsync(_clientId));
        _integration.HealthLevel = null;
        Assert.Equal(WhatsAppHealthLevel.Unknown, (await Sut().GetHealthAsync(_clientId))!.HealthLevel);
    }

    [Fact]
    public async Task Event_list_is_mapped()
    {
        _healthEvents.Setup(h => h.ListForClientAsync(_clientId, 0, 10, It.IsAny<CancellationToken>())).ReturnsAsync(new List<WhatsAppHealthEvent> { new() { Id = Guid.NewGuid(), EventType = "x", Severity = "info" } });
        Assert.Single(await Sut().GetHealthEventsAsync(_clientId, 0, 10));
    }

    [Fact]
    public async Task Events_need_a_connection_and_must_match_the_stored_waba_and_phone()
    {
        var none = new WhatsAppHealthService(Mock.Of<IChannelIntegrationRepository>(), _healthEvents.Object, _uow.Object, _notifier.Object, _events.Object, _notifications.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(() => none.ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, waba: "OTHER")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, phone: "OTHER")));
        _healthEvents.Verify(h => h.Add(It.IsAny<WhatsAppHealthEvent>()), Times.Never);
    }

    [Theory]
    [InlineData(WhatsAppHealthEventType.AccountUpdate, "DISABLED", null, WhatsAppHealthLevel.Problem)]
    [InlineData(WhatsAppHealthEventType.AccountUpdate, "banned", null, WhatsAppHealthLevel.Problem)]
    [InlineData(WhatsAppHealthEventType.AccountUpdate, "VERIFIED", null, WhatsAppHealthLevel.Healthy)]
    [InlineData(WhatsAppHealthEventType.AccountReviewUpdate, "REJECTED", null, WhatsAppHealthLevel.Problem)]
    [InlineData(WhatsAppHealthEventType.AccountReviewUpdate, "APPROVED", null, WhatsAppHealthLevel.Healthy)]
    [InlineData(WhatsAppHealthEventType.PhoneNumberQualityUpdate, null, "RED", WhatsAppHealthLevel.Problem)]
    [InlineData(WhatsAppHealthEventType.PhoneNumberQualityUpdate, null, "YELLOW", WhatsAppHealthLevel.Warning)]
    [InlineData(WhatsAppHealthEventType.PhoneNumberQualityUpdate, "FLAGGED", "GREEN", WhatsAppHealthLevel.Warning)]
    [InlineData(WhatsAppHealthEventType.PhoneNumberQualityUpdate, "CONNECTED", "GREEN", WhatsAppHealthLevel.Healthy)]
    // Meta's real phone_number_quality_update events carry no colour: the event name alone drives the level.
    [InlineData(WhatsAppHealthEventType.PhoneNumberQualityUpdate, "FLAGGED", null, WhatsAppHealthLevel.Warning)]
    [InlineData(WhatsAppHealthEventType.PhoneNumberQualityUpdate, "DOWNGRADE", null, WhatsAppHealthLevel.Warning)]
    [InlineData(WhatsAppHealthEventType.PhoneNumberQualityUpdate, "UNFLAGGED", null, WhatsAppHealthLevel.Healthy)]
    [InlineData(WhatsAppHealthEventType.PhoneNumberQualityUpdate, "UPGRADE", null, WhatsAppHealthLevel.Healthy)]
    [InlineData(WhatsAppHealthEventType.PhoneNumberQualityUpdate, "ONBOARDING", null, WhatsAppHealthLevel.Healthy)]
    [InlineData(WhatsAppHealthEventType.PhoneNumberNameUpdate, "APPROVED", null, WhatsAppHealthLevel.Healthy)]
    [InlineData(WhatsAppHealthEventType.ConnectionUpdate, null, null, WhatsAppHealthLevel.Warning)] // its message is the last error (QA t8-20)
    [InlineData("something_else", "X", "RED", WhatsAppHealthLevel.Healthy)]
    public async Task Health_level_follows_the_event(string type, string? status, string? quality, string expectedLevel)
    {
        var r = await Sut().ApplyHealthEventAsync(Req(type, status, quality, message: "m", code: "c"));
        Assert.Equal(expectedLevel, r.HealthLevel);
        _healthEvents.Verify(h => h.Add(It.Is<WhatsAppHealthEvent>(e => e.EventType == type && e.ClientId == _clientId)), Times.Once);
        _notifier.Verify(n => n.WhatsAppHealthUpdatedAsync(_clientId, _integration.Id, expectedLevel, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // QA t10-02: Meta's real account_update values (as the parser states them) and a rejected display name.
    [Theory]
    [InlineData("disabled", WhatsAppHealthLevel.Problem)]
    [InlineData("restricted", WhatsAppHealthLevel.Problem)]
    [InlineData("scheduled_for_disable", WhatsAppHealthLevel.Warning)]
    [InlineData("violation", WhatsAppHealthLevel.Warning)]
    [InlineData("removed", WhatsAppHealthLevel.Disconnected)]
    [InlineData("reinstated", WhatsAppHealthLevel.Healthy)]
    [InlineData("unrestricted", WhatsAppHealthLevel.Healthy)]
    public async Task Account_states_set_the_level(string state, string level)
    {
        var r = await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, state, phone: null));
        Assert.Equal(level, r.HealthLevel);
    }

    [Fact]
    public async Task A_ban_is_notified_an_unrelated_account_event_keeps_it_and_reinstate_clears_it()
    {
        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, "disabled", phone: null, message: "DISABLE 2026-10-30"));
        Assert.Equal((WhatsAppHealthLevel.Problem, false), (_integration.HealthLevel, _integration.IsHealthy));
        Assert.Equal("Meta disabled this WhatsApp account; messages can't be sent. (DISABLE 2026-10-30)", _integration.LastProblemMessage);
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.IntegrationUnhealthy, It.IsAny<NotificationText>(), null, null, null, _integration.Id, "/WhatsApp/Health", It.IsAny<CancellationToken>()), Times.Once);

        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, "VERIFIED_ACCOUNT", phone: null, at: DateTimeOffset.UtcNow.AddSeconds(1)));
        Assert.Equal((WhatsAppHealthLevel.Problem, "disabled"), (_integration.HealthLevel, _integration.AccountStatus));

        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, "reinstated", phone: null, at: DateTimeOffset.UtcNow.AddSeconds(2)));
        Assert.Equal((WhatsAppHealthLevel.Healthy, "active", (string?)null, (string?)null), (_integration.HealthLevel, _integration.AccountStatus, _integration.LastProblemCode, _integration.LastProblemMessage));
    }

    [Theory]
    [InlineData("BANNED")]
    [InlineData("RESTRICTED")]
    public async Task A_banned_or_restricted_number_is_a_problem(string status)
    {
        var r = await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.PhoneNumberQualityUpdate, status, "GREEN"));
        Assert.Equal(WhatsAppHealthLevel.Problem, r.HealthLevel);
    }

    [Theory]
    [InlineData(ChannelIntegrationStatus.Connected, null, WhatsAppHealthLevel.Healthy)]
    [InlineData(ChannelIntegrationStatus.Connected, "restricted", WhatsAppHealthLevel.Problem)]
    [InlineData(ChannelIntegrationStatus.Disconnected, null, WhatsAppHealthLevel.Disconnected)]
    public void Refresh_sets_health_from_the_rows_state(string status, string? account, string level)
    {
        var row = new ChannelIntegration { Status = status, AccountStatus = account, HealthLevel = "stale", LastProblemMessage = "old" };
        WhatsAppHealthService.Refresh(row);
        Assert.Equal(level, row.HealthLevel);
        Assert.Equal(level == WhatsAppHealthLevel.Healthy, row.LastProblemMessage is null);
    }

    [Fact]
    public async Task A_rejected_display_name_is_a_warning()
    {
        var r = await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.PhoneNumberNameUpdate, "REJECTED", message: "NAME_FORMAT_UNACCEPTABLE"));
        Assert.Equal(WhatsAppHealthLevel.Warning, r.HealthLevel);
        Assert.Contains("display name", _integration.LastProblemMessage);
    }

    [Fact]
    public async Task Unflagged_leaves_no_last_problem()
    {
        _integration.PhoneStatus = "FLAGGED";
        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.PhoneNumberQualityUpdate, "UNFLAGGED", message: "15550100999"));
        Assert.Equal((WhatsAppHealthLevel.Healthy, (string?)null), (_integration.HealthLevel, _integration.LastProblemMessage));
    }

    [Fact]
    public async Task Severity_matches_the_level()
    {
        WhatsAppHealthEvent? stored = null;
        _healthEvents.Setup(h => h.Add(It.IsAny<WhatsAppHealthEvent>())).Callback<WhatsAppHealthEvent>(e => stored = e);
        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, "DISABLED"));
        Assert.Equal(WhatsAppHealthEventSeverity.Error, stored!.Severity);
        _integration.AccountStatus = null;
        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.PhoneNumberQualityUpdate, quality: "YELLOW"));
        Assert.Equal(WhatsAppHealthEventSeverity.Warning, stored.Severity);
        _integration.Status = ChannelIntegrationStatus.Disconnected;
        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, "VERIFIED"));
        Assert.Equal(WhatsAppHealthEventSeverity.Critical, stored.Severity);
        _integration.Status = ChannelIntegrationStatus.Connected;
        _integration.AccountStatus = null;
        _integration.PhoneQualityRating = null;
        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.PhoneNumberNameUpdate, "APPROVED"));
        Assert.Equal(WhatsAppHealthEventSeverity.Info, stored.Severity);
    }

    [Fact]
    public async Task Members_are_notified_only_when_the_connection_turns_unhealthy()
    {
        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, "DISABLED", message: "Account disabled by Meta"));
        _notifications.Verify(n => n.CreateAsync(_clientId, NotificationType.IntegrationUnhealthy, It.Is<NotificationText>(t => TextKit.English(t).Title == "WhatsApp connection needs attention"), null, null, null, _integration.Id, "/WhatsApp/Health", It.IsAny<CancellationToken>()), Times.Once);

        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, "DISABLED")); // already unhealthy
        _notifications.Verify(n => n.CreateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_repeated_event_is_ignored()
    {
        var at = DateTimeOffset.UtcNow.AddMinutes(-3);
        _healthEvents.Setup(h => h.ExistsAsync(_integration.Id, WhatsAppHealthEventType.AccountUpdate, at, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.AccountUpdate, "DISABLED", at: at));
        _healthEvents.Verify(h => h.Add(It.IsAny<WhatsAppHealthEvent>()), Times.Never);
        Assert.Equal(WhatsAppHealthLevel.Healthy, _integration.HealthLevel);
    }

    [Fact]
    public async Task Connection_updates_record_the_error_message()
    {
        await Sut().ApplyHealthEventAsync(Req(WhatsAppHealthEventType.ConnectionUpdate, message: "lost"));
        Assert.Equal("lost", _integration.LastError);
    }
}
