using Omni.App.Business.Services.Campaigns;

namespace Omni.App.Tests.Services;

public class CampaignServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<ICampaignRepository> _campaigns = new();
    private readonly Mock<IWhatsAppTemplateRepository> _templates = new();
    private readonly Mock<IContactRepository> _contacts = new();
    private readonly Mock<IMessageRepository> _messageRows = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IConversationService> _conversations = new();
    private readonly Mock<IMessageService> _messages = new();
    private readonly Mock<ICampaignAudienceService> _audience = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly WhatsAppTemplate _template;

    public CampaignServiceTests()
    {
        _template = new WhatsAppTemplate { Id = Guid.NewGuid(), ClientId = _clientId, Name = "promo", Body = "Hi {{1}}" };
        _templates.Setup(t => t.GetReadOnlyAsync(_clientId, _template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_template);
        _audience.Setup(a => a.GetSkipReasonIfNotContactable(It.IsAny<Contact>())).Returns((string?)null);
        // By default this sender gets the campaign's sending lock.
        _campaigns.Setup(r => r.TryLockForSendingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IAsyncDisposable>());
    }

    private CampaignService Sut() => new(_campaigns.Object, _templates.Object, _contacts.Object, _messageRows.Object, _uow.Object,
        _conversations.Object, _messages.Object, _audience.Object, _entitlements.Object);

    private Campaign MakeCampaign(string status = CampaignStatus.Draft)
    {
        var c = new Campaign { Id = Guid.NewGuid(), ClientId = _clientId, Name = "Spring", Status = status, WhatsAppTemplateId = _template.Id, WhatsAppTemplate = _template, AudienceType = CampaignAudienceType.Custom };
        _campaigns.Setup(r => r.GetAsync(_clientId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        _campaigns.Setup(r => r.GetByIdAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        _campaigns.Setup(r => r.GetWithTemplateAsync(_clientId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        // The "fresh" status read follows the entity, so a test changes it by setting c.Status.
        _campaigns.Setup(r => r.GetStatusReadOnlyAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => c.Status);
        return c;
    }

    private CampaignRecipient Recipient(Campaign c, string status = CampaignRecipientStatus.Pending, Guid? conversationId = null, DateTimeOffset? sentAt = null) => new()
    {
        Id = Guid.NewGuid(), ClientId = _clientId, CampaignId = c.Id, ContactId = Guid.NewGuid(), Status = status, ConversationId = conversationId, SentAt = sentAt, PhoneNumber = "961"
    };

    // ---------------------------------------------------------------- create

    private CreateCampaignRequest Create(IReadOnlyList<Guid>? contactIds = null, string? audienceType = null, string name = "Spring",
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? vars = null) =>
        new(_clientId, name, _template.Id, contactIds ?? [], vars, null, null, audienceType, null);

    [Fact]
    public async Task Create_validates_name_template_audience_and_recipients()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(Create(name: " ")));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(Create([Guid.NewGuid()]) with { WhatsAppTemplateId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(Create([Guid.NewGuid()], "bogus")));
        _contacts.Setup(l => l.ListByIdsAsync(_clientId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<Contact>());
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(Create([Guid.NewGuid()])));
        _entitlements.Verify(e => e.EnsureFeatureAsync(_clientId, EntitlementKeys.Campaigns, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _campaigns.Verify(c => c.Add(It.IsAny<Campaign>()), Times.Never);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"inactiveDays\": \"soon\"}")]
    [InlineData("null")]
    public async Task Create_rejects_malformed_audience_filters_instead_of_targeting_everyone(string filters)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(Create(audienceType: CampaignAudienceType.Custom) with { AudienceFilters = filters }));
        _campaigns.Verify(c => c.Add(It.IsAny<Campaign>()), Times.Never);
        _audience.Verify(a => a.GetEligibleContactsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_stops_when_the_plan_has_no_campaigns_feature()
    {
        _entitlements.Setup(e => e.EnsureFeatureAsync(_clientId, EntitlementKeys.Campaigns, It.IsAny<CancellationToken>())).ThrowsAsync(new EntitlementDeniedException("campaigns", "upgrade"));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().CreateAsync(Create([Guid.NewGuid()])));
    }

    [Fact]
    public async Task Create_with_explicit_contacts_adds_pending_and_skipped_recipients_with_variables()
    {
        var ok = new Contact { Id = Guid.NewGuid(), Phone = "961700" };
        var noPhone = new Contact { Id = Guid.NewGuid(), Phone = null };
        _contacts.Setup(l => l.ListByIdsAsync(_clientId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<Contact> { ok, noPhone });
        _audience.Setup(a => a.GetSkipReasonIfNotContactable(noPhone)).Returns("no_phone");
        var added = new List<CampaignRecipient>();
        _campaigns.Setup(c => c.AddRecipient(It.IsAny<CampaignRecipient>())).Callback<CampaignRecipient>(added.Add);
        Campaign? campaign = null;
        _campaigns.Setup(c => c.Add(It.IsAny<Campaign>())).Callback<Campaign>(c => campaign = c);
        var vars = new Dictionary<Guid, IReadOnlyList<string>> { [ok.Id] = ["Ann"] };

        var r = await Sut().CreateAsync(Create([ok.Id, ok.Id, noPhone.Id], vars: vars));

        Assert.Equal((CampaignStatus.Draft, CampaignType.Custom, CampaignAudienceType.Custom, "promo"), (campaign!.Status, campaign.CampaignType, campaign.AudienceType, r.TemplateName));
        var first = added.Single(a => a.ContactId == ok.Id);
        Assert.Equal((CampaignRecipientStatus.Pending, "[\"Ann\"]", "961700"), (first.Status, first.VariablesJson, first.PhoneNumber));
        var skipped = added.Single(a => a.ContactId == noPhone.Id);
        Assert.Equal((CampaignRecipientStatus.Skipped, "no_phone", ""), (skipped.Status, skipped.SkipReason, skipped.PhoneNumber));
        _contacts.Verify(l => l.ListByIdsAsync(_clientId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_without_explicit_contacts_resolves_the_audience()
    {
        var contact = new Contact { Id = Guid.NewGuid(), Phone = "961" };
        _audience.Setup(a => a.GetEligibleContactsAsync(_clientId, CampaignAudienceType.AllEligible, "{}", It.IsAny<CancellationToken>())).ReturnsAsync(new List<Contact> { contact });
        var r = await Sut().CreateAsync(Create(audienceType: CampaignAudienceType.AllEligible) with { AudienceFilters = "{}", CampaignType = CampaignType.Promotion });
        Assert.Equal((CampaignAudienceType.AllEligible, CampaignType.Promotion), (r.AudienceType, r.CampaignType));
    }

    // ---------------------------------------------------------------- schedule / send / cancel

    [Fact]
    public async Task Schedule_only_drafts()
    {
        Assert.Null(await Sut().ScheduleAsync(_clientId, Guid.NewGuid(), DateTimeOffset.UtcNow));
        var running = MakeCampaign(CampaignStatus.Running);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ScheduleAsync(_clientId, running.Id, DateTimeOffset.UtcNow));
        var draft = MakeCampaign();
        var when = DateTimeOffset.UtcNow.AddDays(1);
        var r = await Sut().ScheduleAsync(_clientId, draft.Id, when);
        Assert.Equal((CampaignStatus.Scheduled, when), (r!.Status, draft.ScheduledAt));
    }

    [Fact]
    public async Task Send_queues_pending_recipients_starts_the_campaign_and_processes_a_batch()
    {
        var c = MakeCampaign();
        var r1 = Recipient(c);
        var r2 = Recipient(c);
        _campaigns.Setup(x => x.ListRecipientsInStatusAsync(c.Id, It.Is<IReadOnlyCollection<string>>(s => s.Contains(CampaignRecipientStatus.Pending)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CampaignRecipient> { r1, r2 });
        _campaigns.Setup(x => x.ListQueuedBatchAsync(c.Id, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { r1, r2 });
        var conv = new ConversationResponse(Guid.NewGuid(), _clientId, Guid.NewGuid(), "whatsapp", "active", "ai", true, false, null, null, null, false, DateTimeOffset.UtcNow);
        _conversations.Setup(x => x.GetOrCreateForContactAsync(_clientId, It.IsAny<Guid>(), ConversationChannel.WhatsApp, It.IsAny<CancellationToken>())).ReturnsAsync(conv);
        var msgId = Guid.NewGuid();
        _messages.Setup(m => m.SendCampaignTemplateAsync(_clientId, conv.Id, _template.Id, It.IsAny<IReadOnlyList<string>>(), c.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, Guid _, IReadOnlyList<string> _, Guid _, Guid _, CancellationToken _) => new MessageResponse(msgId, conv.Id, conv.ContactId, "outbound", "member", "campaign", "whatsapp", "template", "x", null, false, null, null, null, null, DateTimeOffset.UtcNow, null, null, null, null, null, null, null));
        _messageRows.Setup(m => m.GetExternalIdAsync(msgId, It.IsAny<CancellationToken>())).ReturnsAsync("wamid.1");
        _campaigns.SetupSequence(x => x.CountRecipientsInStatusAsync(c.Id, CampaignRecipientStatus.Queued, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var result = await Sut().SendAsync(_clientId, c.Id);

        Assert.Equal((2, 2, 0, 0, true), (result!.Processed, result.Succeeded, result.Failed, result.RemainingQueued, result.CampaignCompleted));
        Assert.Equal((CampaignStatus.Completed, true), (c.Status, c.StartedAt is not null && c.CompletedAt is not null));
        Assert.All(new[] { r1, r2 }, r => Assert.Equal((CampaignRecipientStatus.Sent, "wamid.1", conv.Id), (r.Status, r.ExternalMessageId, r.ConversationId)));
        Assert.NotNull(r1.QueuedAt);
    }

    [Fact]
    public async Task Send_guards_status_and_missing_campaign()
    {
        Assert.Null(await Sut().SendAsync(_clientId, Guid.NewGuid()));
        var done = MakeCampaign(CampaignStatus.Completed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendAsync(_clientId, done.Id));
        var cancelled = MakeCampaign(CampaignStatus.Cancelled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendAsync(_clientId, cancelled.Id));
    }

    [Fact]
    public async Task Send_with_only_skipped_recipients_completes_immediately()
    {
        var c = MakeCampaign();
        _campaigns.Setup(x => x.ListRecipientsInStatusAsync(c.Id, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient>());
        _campaigns.Setup(x => x.ListQueuedBatchAsync(c.Id, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient>());
        var result = await Sut().SendAsync(_clientId, c.Id);
        Assert.True(result!.CampaignCompleted);
        Assert.Equal(CampaignStatus.Completed, c.Status);
    }

    [Fact]
    public async Task ProcessBatch_on_a_non_running_campaign_just_reports_progress()
    {
        Assert.Null(await Sut().ProcessBatchAsync(_clientId, Guid.NewGuid()));
        var scheduled = MakeCampaign(CampaignStatus.Scheduled);
        _campaigns.Setup(x => x.CountRecipientsInStatusAsync(scheduled.Id, CampaignRecipientStatus.Queued, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        var r = await Sut().ProcessBatchAsync(_clientId, scheduled.Id);
        Assert.Equal((0, 3, false), (r!.Processed, r.RemainingQueued, r.CampaignCompleted));
        var done = MakeCampaign(CampaignStatus.Completed);
        Assert.True((await Sut().ProcessBatchAsync(_clientId, done.Id))!.CampaignCompleted);
    }

    [Fact]
    public async Task A_failed_recipient_is_recorded_and_the_batch_continues()
    {
        var c = MakeCampaign(CampaignStatus.Running);
        var bad = Recipient(c, CampaignRecipientStatus.Queued);
        var good = Recipient(c, CampaignRecipientStatus.Queued);
        _campaigns.Setup(x => x.ListQueuedBatchAsync(c.Id, 5, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { bad, good });
        _campaigns.Setup(x => x.CountRecipientsInStatusAsync(c.Id, CampaignRecipientStatus.Queued, It.IsAny<CancellationToken>())).ReturnsAsync(4);
        var conv = new ConversationResponse(Guid.NewGuid(), _clientId, Guid.NewGuid(), "whatsapp", "active", "ai", true, false, null, null, null, false, DateTimeOffset.UtcNow);
        _conversations.Setup(x => x.GetOrCreateForContactAsync(_clientId, bad.ContactId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("no window"));
        _conversations.Setup(x => x.GetOrCreateForContactAsync(_clientId, good.ContactId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(conv);

        var r = await Sut().ProcessBatchAsync(_clientId, c.Id, 5);

        Assert.Equal((2, 1, 1, 4, false), (r!.Processed, r.Succeeded, r.Failed, r.RemainingQueued, r.CampaignCompleted));
        Assert.Equal((CampaignRecipientStatus.Failed, "no window"), (bad.Status, bad.FailureReason));
        Assert.Equal(CampaignRecipientStatus.Sent, good.Status);
        Assert.Equal(CampaignStatus.Running, c.Status);
    }

    [Fact]
    public async Task Recipient_variables_are_passed_to_the_template_and_a_missing_message_is_tolerated()
    {
        var c = MakeCampaign(CampaignStatus.Running);
        var r = Recipient(c, CampaignRecipientStatus.Queued);
        r.VariablesJson = "[\"Ann\",\"20%\"]";
        _campaigns.Setup(x => x.ListQueuedBatchAsync(c.Id, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { r });
        var conv = new ConversationResponse(Guid.NewGuid(), _clientId, r.ContactId, "whatsapp", "active", "ai", true, false, null, null, null, false, DateTimeOffset.UtcNow);
        _conversations.Setup(x => x.GetOrCreateForContactAsync(_clientId, r.ContactId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(conv);
        await Sut().ProcessBatchAsync(_clientId, c.Id);
        _messages.Verify(m => m.SendCampaignTemplateAsync(_clientId, conv.Id, _template.Id, It.Is<IReadOnlyList<string>>(v => v.SequenceEqual(new[] { "Ann", "20%" })), c.Id, r.Id, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(r.MessageId);
        Assert.Equal(CampaignRecipientStatus.Sent, r.Status);
    }

    [Fact]
    public async Task Campaign_without_a_template_fails_each_recipient()
    {
        var c = MakeCampaign(CampaignStatus.Running);
        c.WhatsAppTemplateId = null;
        var r = Recipient(c, CampaignRecipientStatus.Queued);
        _campaigns.Setup(x => x.ListQueuedBatchAsync(c.Id, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { r });
        var result = await Sut().ProcessBatchAsync(_clientId, c.Id);
        Assert.Equal(1, result!.Failed);
        Assert.Contains("no WhatsApp template", r.FailureReason);
    }

    [Fact]
    public async Task Insufficient_funds_or_entitlement_errors_stop_the_batch_and_leave_the_recipient_queued()
    {
        var c = MakeCampaign(CampaignStatus.Running);
        var r = Recipient(c, CampaignRecipientStatus.Queued);
        _campaigns.Setup(x => x.ListQueuedBatchAsync(c.Id, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { r });
        _conversations.Setup(x => x.GetOrCreateForContactAsync(_clientId, r.ContactId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BillingDeniedException(UsageFailureReason.InsufficientFunds, "out of credit"));
        await Assert.ThrowsAsync<BillingDeniedException>(() => Sut().ProcessBatchAsync(_clientId, c.Id));
        Assert.Equal(CampaignRecipientStatus.Queued, r.Status);

        _conversations.Setup(x => x.GetOrCreateForContactAsync(_clientId, r.ContactId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new EntitlementDeniedException("messages", "plan"));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().ProcessBatchAsync(_clientId, c.Id));
        Assert.Equal(CampaignRecipientStatus.Queued, r.Status);
    }

    [Fact]
    public async Task Other_billing_denials_fail_only_that_recipient()
    {
        var c = MakeCampaign(CampaignStatus.Running);
        var r = Recipient(c, CampaignRecipientStatus.Queued);
        _campaigns.Setup(x => x.ListQueuedBatchAsync(c.Id, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { r });
        _conversations.Setup(x => x.GetOrCreateForContactAsync(_clientId, r.ContactId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BillingDeniedException(UsageFailureReason.RateNotFound, "no sub"));
        var result = await Sut().ProcessBatchAsync(_clientId, c.Id);
        Assert.Equal(1, result!.Failed);
    }

    [Fact]
    public async Task Cancel_fails_outstanding_recipients_and_rejects_finished_campaigns()
    {
        Assert.Null(await Sut().CancelAsync(_clientId, Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().CancelAsync(_clientId, MakeCampaign(CampaignStatus.Completed).Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().CancelAsync(_clientId, MakeCampaign(CampaignStatus.Cancelled).Id));

        var c = MakeCampaign(CampaignStatus.Running);
        var p = Recipient(c, CampaignRecipientStatus.Pending);
        var q = Recipient(c, CampaignRecipientStatus.Queued);
        _campaigns.Setup(x => x.ListRecipientsInStatusAsync(c.Id, It.Is<IReadOnlyCollection<string>>(s => s.Contains(CampaignRecipientStatus.Queued)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CampaignRecipient> { p, q });
        var r = await Sut().CancelAsync(_clientId, c.Id);
        Assert.Equal(CampaignStatus.Cancelled, r!.Status);
        Assert.All(new[] { p, q }, x => Assert.Equal((CampaignRecipientStatus.Failed, "Campaign cancelled."), (x.Status, x.FailureReason)));
    }

    // ---------------------------------------------------------------- stop / resume / background sending

    /// <summary>Every contact gets a WhatsApp conversation, so each template send goes through.</summary>
    private void SendsSucceed()
    {
        _conversations.Setup(x => x.GetOrCreateForContactAsync(_clientId, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid contactId, string _, CancellationToken _) =>
                new ConversationResponse(Guid.NewGuid(), _clientId, contactId, "whatsapp", "active", "ai", true, false, null, null, null, false, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Stop_pauses_a_scheduled_or_running_campaign_and_rejects_the_rest()
    {
        Assert.Null(await Sut().StopAsync(_clientId, Guid.NewGuid()));
        foreach (var status in new[] { CampaignStatus.Draft, CampaignStatus.Paused, CampaignStatus.Completed, CampaignStatus.Cancelled })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().StopAsync(_clientId, MakeCampaign(status).Id));
        }
        _campaigns.Verify(r => r.TryChangeStatusAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);

        foreach (var status in new[] { CampaignStatus.Scheduled, CampaignStatus.Running })
        {
            var c = MakeCampaign(status);
            _campaigns.Setup(r => r.TryChangeStatusAsync(_clientId, c.Id,
                    It.Is<IReadOnlyCollection<string>>(s => s.Contains(CampaignStatus.Scheduled) && s.Contains(CampaignStatus.Running) && s.Count == 2),
                    CampaignStatus.Paused, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var r = await Sut().StopAsync(_clientId, c.Id);

            Assert.Equal(CampaignStatus.Paused, r!.Status);
            // Set-based, so nothing is saved through the unit of work (a racing completion can't be overwritten).
            _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Fact]
    public async Task Stop_refuses_when_the_campaign_finished_in_the_meantime()
    {
        var c = MakeCampaign(CampaignStatus.Running);
        _campaigns.Setup(r => r.TryChangeStatusAsync(_clientId, c.Id, It.IsAny<IReadOnlyCollection<string>>(), CampaignStatus.Paused,
            It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().StopAsync(_clientId, c.Id));
        Assert.Contains("finished or changed", ex.Message);
        Assert.Equal(CampaignStatus.Running, c.Status);
    }

    [Fact]
    public async Task Send_on_a_stopped_campaign_resumes_it_queues_leftovers_and_keeps_the_first_start_time()
    {
        var c = MakeCampaign(CampaignStatus.Paused);
        var started = DateTimeOffset.UtcNow.AddHours(-2);
        c.StartedAt = started;
        var pending = Recipient(c, CampaignRecipientStatus.Pending);
        _campaigns.Setup(x => x.ListRecipientsInStatusAsync(c.Id, It.Is<IReadOnlyCollection<string>>(s => s.Contains(CampaignRecipientStatus.Pending)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CampaignRecipient> { pending });
        _campaigns.Setup(x => x.ListQueuedBatchAsync(c.Id, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { pending });
        _campaigns.Setup(x => x.CountRecipientsInStatusAsync(c.Id, CampaignRecipientStatus.Queued, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        SendsSucceed();

        var result = await Sut().SendAsync(_clientId, c.Id);

        Assert.Equal((1, true), (result!.Succeeded, result.CampaignCompleted));
        Assert.Equal((CampaignStatus.Completed, started), (c.Status, c.StartedAt));
        Assert.Equal(CampaignRecipientStatus.Sent, pending.Status);
        Assert.NotNull(pending.QueuedAt);
    }

    [Fact]
    public async Task Stopping_mid_batch_halts_before_the_next_message_and_the_campaign_stays_stopped()
    {
        var c = MakeCampaign(CampaignStatus.Running);
        var first = Recipient(c, CampaignRecipientStatus.Queued);
        var second = Recipient(c, CampaignRecipientStatus.Queued);
        _campaigns.Setup(x => x.ListQueuedBatchAsync(c.Id, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { first, second });
        _campaigns.Setup(x => x.CountRecipientsInStatusAsync(c.Id, CampaignRecipientStatus.Queued, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        SendsSucceed();
        // A member press Stop while the first message is going out.
        _messages.Setup(m => m.SendCampaignTemplateAsync(_clientId, It.IsAny<Guid>(), _template.Id, It.IsAny<IReadOnlyList<string>>(), c.Id, first.Id, It.IsAny<CancellationToken>()))
            .Callback(() => c.Status = CampaignStatus.Paused)
            .ReturnsAsync((MessageResponse?)null);

        var result = await Sut().ProcessBatchAsync(_clientId, c.Id);

        Assert.Equal((1, 1, 0, 1, false), (result!.Processed, result.Succeeded, result.Failed, result.RemainingQueued, result.CampaignCompleted));
        Assert.Equal((CampaignRecipientStatus.Sent, CampaignRecipientStatus.Queued), (first.Status, second.Status));
        _messages.Verify(m => m.SendCampaignTemplateAsync(_clientId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), c.Id, second.Id, It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(CampaignStatus.Paused, c.Status);
    }

    [Fact]
    public async Task A_campaign_cancelled_during_its_last_batch_is_not_marked_completed()
    {
        var c = MakeCampaign(CampaignStatus.Running);
        var only = Recipient(c, CampaignRecipientStatus.Queued);
        _campaigns.Setup(x => x.ListQueuedBatchAsync(c.Id, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { only });
        _campaigns.Setup(x => x.CountRecipientsInStatusAsync(c.Id, CampaignRecipientStatus.Queued, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        SendsSucceed();
        _messages.Setup(m => m.SendCampaignTemplateAsync(_clientId, It.IsAny<Guid>(), _template.Id, It.IsAny<IReadOnlyList<string>>(), c.Id, only.Id, It.IsAny<CancellationToken>()))
            .Callback(() => c.Status = CampaignStatus.Cancelled)
            .ReturnsAsync((MessageResponse?)null);

        var result = await Sut().ProcessBatchAsync(_clientId, c.Id);

        Assert.False(result!.CampaignCompleted);
        Assert.Equal(CampaignStatus.Cancelled, c.Status);
        Assert.Null(c.CompletedAt);
    }

    [Fact]
    public async Task While_another_sender_holds_the_campaign_nothing_is_sent()
    {
        var c = MakeCampaign(CampaignStatus.Running);
        _campaigns.Setup(r => r.TryLockForSendingAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync((IAsyncDisposable?)null);
        _campaigns.Setup(x => x.CountRecipientsInStatusAsync(c.Id, CampaignRecipientStatus.Queued, It.IsAny<CancellationToken>())).ReturnsAsync(7);

        var result = await Sut().ProcessBatchAsync(_clientId, c.Id);

        Assert.Equal((0, 7, false), (result!.Processed, result.RemainingQueued, result.CampaignCompleted));
        _campaigns.Verify(x => x.ListQueuedBatchAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(CampaignStatus.Running, c.Status);
    }

    [Fact]
    public async Task The_sending_lock_is_released_after_a_batch_even_when_it_stops_on_billing()
    {
        var c = MakeCampaign(CampaignStatus.Running);
        var released = 0;
        var handle = new Mock<IAsyncDisposable>();
        handle.Setup(h => h.DisposeAsync()).Callback(() => released++).Returns(ValueTask.CompletedTask);
        _campaigns.Setup(r => r.TryLockForSendingAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(handle.Object);
        var r = Recipient(c, CampaignRecipientStatus.Queued);
        _campaigns.Setup(x => x.ListQueuedBatchAsync(c.Id, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { r });
        _campaigns.Setup(x => x.CountRecipientsInStatusAsync(c.Id, CampaignRecipientStatus.Queued, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await Sut().ProcessBatchAsync(_clientId, c.Id);
        Assert.Equal(1, released);

        _conversations.Setup(x => x.GetOrCreateForContactAsync(_clientId, r.ContactId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BillingDeniedException(UsageFailureReason.InsufficientFunds, "out of credit"));
        r.Status = CampaignRecipientStatus.Queued;
        await Assert.ThrowsAsync<BillingDeniedException>(() => Sut().ProcessBatchAsync(_clientId, c.Id));
        Assert.Equal(2, released);
    }

    [Fact]
    public async Task SendDue_starts_a_scheduled_campaign_only_once_its_time_has_come()
    {
        Assert.Null(await Sut().SendDueAsync(_clientId, Guid.NewGuid(), 20));

        var early = MakeCampaign(CampaignStatus.Scheduled);
        early.ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(10);
        Assert.Null(await Sut().SendDueAsync(_clientId, early.Id, 20));
        Assert.Equal(CampaignStatus.Scheduled, early.Status);

        var noTime = MakeCampaign(CampaignStatus.Scheduled);
        Assert.Null(await Sut().SendDueAsync(_clientId, noTime.Id, 20));

        var due = MakeCampaign(CampaignStatus.Scheduled);
        due.ScheduledAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var p = Recipient(due, CampaignRecipientStatus.Pending);
        _campaigns.Setup(x => x.ListRecipientsInStatusAsync(due.Id, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { p });
        _campaigns.Setup(x => x.ListQueuedBatchAsync(due.Id, 5, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { p });
        _campaigns.Setup(x => x.CountRecipientsInStatusAsync(due.Id, CampaignRecipientStatus.Queued, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        SendsSucceed();

        var result = await Sut().SendDueAsync(_clientId, due.Id, 5);

        Assert.Equal((1, true), (result!.Succeeded, result.CampaignCompleted));
        Assert.NotNull(due.StartedAt);
        Assert.Equal(CampaignRecipientStatus.Sent, p.Status);
    }

    [Fact]
    public async Task SendDue_continues_running_campaigns_and_leaves_stopped_or_finished_ones_alone()
    {
        foreach (var status in new[] { CampaignStatus.Paused, CampaignStatus.Draft, CampaignStatus.Cancelled, CampaignStatus.Completed })
        {
            var c = MakeCampaign(status);
            Assert.Null(await Sut().SendDueAsync(_clientId, c.Id, 20));
            Assert.Equal(status, c.Status);
        }
        _messages.Verify(m => m.SendCampaignTemplateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        var running = MakeCampaign(CampaignStatus.Running);
        var q = Recipient(running, CampaignRecipientStatus.Queued);
        _campaigns.Setup(x => x.ListQueuedBatchAsync(running.Id, 3, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { q });
        _campaigns.Setup(x => x.CountRecipientsInStatusAsync(running.Id, CampaignRecipientStatus.Queued, It.IsAny<CancellationToken>())).ReturnsAsync(4);
        SendsSucceed();

        var result = await Sut().SendDueAsync(_clientId, running.Id, 3);

        Assert.Equal((1, 4, false), (result!.Succeeded, result.RemainingQueued, result.CampaignCompleted));
    }

    [Fact]
    public async Task ListDue_asks_the_repository_for_campaigns_due_now()
    {
        var due = new List<DueCampaign> { new(Guid.NewGuid(), _clientId, CampaignStatus.Running) };
        _campaigns.Setup(r => r.ListDueForSendingReadOnlyAsync(It.Is<DateTimeOffset>(t => Math.Abs((t - DateTimeOffset.UtcNow).TotalSeconds) < 5), It.IsAny<CancellationToken>()))
            .ReturnsAsync(due);
        Assert.Same(due, await Sut().ListDueAsync());
    }

    [Fact]
    public async Task Cancel_also_ends_a_stopped_campaign()
    {
        var c = MakeCampaign(CampaignStatus.Paused);
        var q = Recipient(c, CampaignRecipientStatus.Queued);
        _campaigns.Setup(x => x.ListRecipientsInStatusAsync(c.Id, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { q });
        var r = await Sut().CancelAsync(_clientId, c.Id);
        Assert.Equal((CampaignStatus.Cancelled, CampaignRecipientStatus.Failed), (r!.Status, q.Status));
    }

    // ---------------------------------------------------------------- reads / stats

    [Fact]
    public async Task List_computes_per_campaign_stats_including_replies_after_sending()
    {
        _campaigns.Setup(r => r.ListWithTemplateAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Campaign>());
        Assert.Empty(await Sut().ListAsync(_clientId));

        var c = MakeCampaign();
        var convId = Guid.NewGuid();
        var sentAt = DateTimeOffset.UtcNow.AddHours(-2);
        var recipients = new List<CampaignRecipient>
        {
            Recipient(c, CampaignRecipientStatus.Read, convId, sentAt),
            Recipient(c, CampaignRecipientStatus.Delivered, Guid.NewGuid(), sentAt),
            Recipient(c, CampaignRecipientStatus.Failed),
            Recipient(c, CampaignRecipientStatus.Skipped),
            Recipient(c, CampaignRecipientStatus.Pending),
            Recipient(c, CampaignRecipientStatus.Queued)
        };
        recipients[1].BookedAt = DateTimeOffset.UtcNow;
        _campaigns.Setup(r => r.ListWithTemplateAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Campaign> { c });
        _campaigns.Setup(r => r.ListRecipientsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(recipients);
        _messageRows.Setup(m => m.ListInboundCustomerMessageTimesAsync(_clientId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, DateTimeOffset)> { (convId, sentAt.AddMinutes(5)), (recipients[1].ConversationId!.Value, sentAt.AddMinutes(-5)) });

        var row = Assert.Single(await Sut().ListAsync(_clientId));

        Assert.Equal((6, 2, 2, 1, 1, 1), (row.TotalRecipients, row.Sent, row.Delivered, row.Failed, row.Read, row.Replied));
        Assert.Equal(("promo", true), (row.TemplateName, row.ManuallySelected));
    }

    [Fact]
    public async Task GetById_returns_details_with_stats_and_recipient_rows()
    {
        Assert.Null(await Sut().GetByIdAsync(_clientId, Guid.NewGuid()));
        var c = MakeCampaign();
        var r = Recipient(c, CampaignRecipientStatus.Sent);
        r.Contact = new Contact { FullName = "Ann" };
        _campaigns.Setup(x => x.ListRecipientsWithContactAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { r });
        var d = await Sut().GetByIdAsync(_clientId, c.Id);
        Assert.Equal((1, 1, "Hi {{1}}", "Ann"), (d!.Stats.TotalRecipients, d.Stats.Sent, d.TemplateBody, d.Recipients[0].ContactFullName));
    }
}

public class CampaignAudienceServiceTests
{
    private readonly Mock<ICampaignAudienceRepository> _repo = new();
    private readonly Mock<IConfigManager> _config = new();

    private CampaignAudienceService Sut() => new(_repo.Object, _config.Object);

    [Theory]
    [InlineData(null, true, false, "no_phone")]
    [InlineData("  ", true, false, "no_phone")]
    [InlineData("961", true, true, "opted_out")]
    [InlineData("961", false, false, "marketing_opt_in_false")]
    [InlineData("961", true, false, null)]
    public void Skip_reason_priority(string? phone, bool optIn, bool optedOut, string? expected) =>
        Assert.Equal(expected, Sut().GetSkipReasonIfNotContactable(new Contact { Phone = phone, MarketingOptIn = optIn, OptedOutAt = optedOut ? DateTimeOffset.UtcNow : null }));

    [Fact]
    public async Task Eligible_contacts_and_count_pass_parsed_filters_and_the_configured_default()
    {
        _config.SetupGet(c => c.CampaignsDefaultInactiveDays).Returns(45);
        var client = Guid.NewGuid();
        await Sut().GetEligibleContactsAsync(client, CampaignAudienceType.AllEligible, "{\"inactiveDays\":10,\"cities\":[\"Beirut\"]}");
        _repo.Verify(r => r.ListEligibleContactsAsync(client, CampaignAudienceType.AllEligible,
            It.Is<CampaignAudienceFilters>(f => f.InactiveDays == 10 && f.Cities!.Single() == "Beirut"), 45, It.IsAny<CancellationToken>()), Times.Once);
        await Sut().GetMatchingCountAsync(client, CampaignAudienceType.Custom, null);
        _repo.Verify(r => r.CountEligibleContactsAsync(client, CampaignAudienceType.Custom, It.Is<CampaignAudienceFilters>(f => f.InactiveDays == null), 45, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public void Filters_parse_leniently(string? json) => Assert.Null(CampaignAudienceFilters.Parse(json).InactiveDays);

    [Fact]
    public void Filters_parse_all_fields_case_insensitively()
    {
        var f = CampaignAudienceFilters.Parse("{\"InactiveDays\":3,\"stageKeys\":[\"new\"],\"createdAfter\":\"2026-01-01T00:00:00Z\"}");
        Assert.Equal((3, "new"), (f.InactiveDays, f.StageKeys!.Single()));
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), f.CreatedAfter);
    }
}
