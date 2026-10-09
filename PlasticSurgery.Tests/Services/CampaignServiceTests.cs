using PlasticSurgery.Business.Services.Campaigns;

namespace PlasticSurgery.Tests.Services;

public class CampaignServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<ICampaignRepository> _campaigns = new();
    private readonly Mock<IWhatsAppTemplateRepository> _templates = new();
    private readonly Mock<ILeadRepository> _leads = new();
    private readonly Mock<IMessageRepository> _messageRows = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IConversationService> _conversations = new();
    private readonly Mock<IMessageService> _messages = new();
    private readonly Mock<ICampaignAudienceService> _audience = new();
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly WhatsAppTemplate _template;

    public CampaignServiceTests()
    {
        _template = new WhatsAppTemplate { Id = Guid.NewGuid(), ClinicId = _clinicId, Name = "promo", Body = "Hi {{1}}" };
        _templates.Setup(t => t.GetAsync(_clinicId, _template.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_template);
        _audience.Setup(a => a.GetSkipReasonIfNotContactable(It.IsAny<Lead>())).Returns((string?)null);
    }

    private CampaignService Sut() => new(_campaigns.Object, _templates.Object, _leads.Object, _messageRows.Object, _uow.Object,
        _conversations.Object, _messages.Object, _audience.Object, _entitlements.Object);

    private Campaign MakeCampaign(string status = CampaignStatus.Draft)
    {
        var c = new Campaign { Id = Guid.NewGuid(), ClinicId = _clinicId, Name = "Spring", Status = status, WhatsAppTemplateId = _template.Id, WhatsAppTemplate = _template, AudienceType = CampaignAudienceType.Custom };
        _campaigns.Setup(r => r.GetAsync(_clinicId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        _campaigns.Setup(r => r.GetByIdAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        _campaigns.Setup(r => r.GetWithTemplateAsync(_clinicId, c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        return c;
    }

    private CampaignRecipient Recipient(Campaign c, string status = CampaignRecipientStatus.Pending, Guid? conversationId = null, DateTimeOffset? sentAt = null) => new()
    {
        Id = Guid.NewGuid(), ClinicId = _clinicId, CampaignId = c.Id, LeadId = Guid.NewGuid(), Status = status, ConversationId = conversationId, SentAt = sentAt, PhoneNumber = "961"
    };

    // ---------------------------------------------------------------- create

    private CreateCampaignRequest Create(IReadOnlyList<Guid>? leadIds = null, string? audienceType = null, string name = "Spring",
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? vars = null) =>
        new(_clinicId, name, _template.Id, leadIds ?? [], vars, null, null, audienceType, null);

    [Fact]
    public async Task Create_validates_name_template_audience_and_recipients()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(Create(name: " ")));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(Create([Guid.NewGuid()]) with { WhatsAppTemplateId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(Create([Guid.NewGuid()], "bogus")));
        _leads.Setup(l => l.ListByIdsAsync(_clinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<Lead>());
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateAsync(Create([Guid.NewGuid()])));
        _entitlements.Verify(e => e.EnsureFeatureAsync(_clinicId, EntitlementKeys.Campaigns, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
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
        _audience.Verify(a => a.GetEligibleLeadsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_stops_when_the_plan_has_no_campaigns_feature()
    {
        _entitlements.Setup(e => e.EnsureFeatureAsync(_clinicId, EntitlementKeys.Campaigns, It.IsAny<CancellationToken>())).ThrowsAsync(new EntitlementDeniedException("campaigns", "upgrade"));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().CreateAsync(Create([Guid.NewGuid()])));
    }

    [Fact]
    public async Task Create_with_explicit_leads_adds_pending_and_skipped_recipients_with_variables()
    {
        var ok = new Lead { Id = Guid.NewGuid(), Phone = "961700" };
        var noPhone = new Lead { Id = Guid.NewGuid(), Phone = null };
        _leads.Setup(l => l.ListByIdsAsync(_clinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<Lead> { ok, noPhone });
        _audience.Setup(a => a.GetSkipReasonIfNotContactable(noPhone)).Returns("no_phone");
        var added = new List<CampaignRecipient>();
        _campaigns.Setup(c => c.AddRecipient(It.IsAny<CampaignRecipient>())).Callback<CampaignRecipient>(added.Add);
        Campaign? campaign = null;
        _campaigns.Setup(c => c.Add(It.IsAny<Campaign>())).Callback<Campaign>(c => campaign = c);
        var vars = new Dictionary<Guid, IReadOnlyList<string>> { [ok.Id] = ["Ann"] };

        var r = await Sut().CreateAsync(Create([ok.Id, ok.Id, noPhone.Id], vars: vars));

        Assert.Equal((CampaignStatus.Draft, CampaignType.Custom, CampaignAudienceType.Custom, "promo"), (campaign!.Status, campaign.CampaignType, campaign.AudienceType, r.TemplateName));
        var first = added.Single(a => a.LeadId == ok.Id);
        Assert.Equal((CampaignRecipientStatus.Pending, "[\"Ann\"]", "961700"), (first.Status, first.VariablesJson, first.PhoneNumber));
        var skipped = added.Single(a => a.LeadId == noPhone.Id);
        Assert.Equal((CampaignRecipientStatus.Skipped, "no_phone", ""), (skipped.Status, skipped.SkipReason, skipped.PhoneNumber));
        _leads.Verify(l => l.ListByIdsAsync(_clinicId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_without_explicit_leads_resolves_the_audience()
    {
        var lead = new Lead { Id = Guid.NewGuid(), Phone = "961" };
        _audience.Setup(a => a.GetEligibleLeadsAsync(_clinicId, CampaignAudienceType.AllEligible, "{}", It.IsAny<CancellationToken>())).ReturnsAsync(new List<Lead> { lead });
        var r = await Sut().CreateAsync(Create(audienceType: CampaignAudienceType.AllEligible) with { AudienceFilters = "{}", CampaignType = CampaignType.Promotion });
        Assert.Equal((CampaignAudienceType.AllEligible, CampaignType.Promotion), (r.AudienceType, r.CampaignType));
    }

    // ---------------------------------------------------------------- schedule / send / cancel

    [Fact]
    public async Task Schedule_only_drafts()
    {
        Assert.Null(await Sut().ScheduleAsync(_clinicId, Guid.NewGuid(), DateTimeOffset.UtcNow));
        var running = MakeCampaign(CampaignStatus.Running);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().ScheduleAsync(_clinicId, running.Id, DateTimeOffset.UtcNow));
        var draft = MakeCampaign();
        var when = DateTimeOffset.UtcNow.AddDays(1);
        var r = await Sut().ScheduleAsync(_clinicId, draft.Id, when);
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
        var conv = new ConversationResponse(Guid.NewGuid(), _clinicId, Guid.NewGuid(), "whatsapp", "active", "ai", true, false, null, null, null, false, DateTimeOffset.UtcNow);
        _conversations.Setup(x => x.GetOrCreateForLeadAsync(_clinicId, It.IsAny<Guid>(), ConversationChannel.WhatsApp, It.IsAny<CancellationToken>())).ReturnsAsync(conv);
        var msgId = Guid.NewGuid();
        _messages.Setup(m => m.SendCampaignTemplateAsync(_clinicId, conv.Id, _template.Id, It.IsAny<IReadOnlyList<string>>(), c.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid _, Guid _, IReadOnlyList<string> _, Guid _, Guid _, CancellationToken _) => new MessageResponse(msgId, conv.Id, conv.LeadId, "outbound", "staff", "campaign", "whatsapp", "template", "x", null, false, null, null, null, null, DateTimeOffset.UtcNow, null, null, null, null, null, null, null));
        _messageRows.Setup(m => m.GetExternalIdAsync(msgId, It.IsAny<CancellationToken>())).ReturnsAsync("wamid.1");
        _campaigns.SetupSequence(x => x.CountRecipientsInStatusAsync(c.Id, CampaignRecipientStatus.Queued, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var result = await Sut().SendAsync(_clinicId, c.Id);

        Assert.Equal((2, 2, 0, 0, true), (result!.Processed, result.Succeeded, result.Failed, result.RemainingQueued, result.CampaignCompleted));
        Assert.Equal((CampaignStatus.Completed, true), (c.Status, c.StartedAt is not null && c.CompletedAt is not null));
        Assert.All(new[] { r1, r2 }, r => Assert.Equal((CampaignRecipientStatus.Sent, "wamid.1", conv.Id), (r.Status, r.ExternalMessageId, r.ConversationId)));
        Assert.NotNull(r1.QueuedAt);
    }

    [Fact]
    public async Task Send_guards_status_and_missing_campaign()
    {
        Assert.Null(await Sut().SendAsync(_clinicId, Guid.NewGuid()));
        var done = MakeCampaign(CampaignStatus.Completed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendAsync(_clinicId, done.Id));
        var cancelled = MakeCampaign(CampaignStatus.Cancelled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().SendAsync(_clinicId, cancelled.Id));
    }

    [Fact]
    public async Task Send_with_only_skipped_recipients_completes_immediately()
    {
        var c = MakeCampaign();
        _campaigns.Setup(x => x.ListRecipientsInStatusAsync(c.Id, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient>());
        _campaigns.Setup(x => x.ListQueuedBatchAsync(c.Id, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient>());
        var result = await Sut().SendAsync(_clinicId, c.Id);
        Assert.True(result!.CampaignCompleted);
        Assert.Equal(CampaignStatus.Completed, c.Status);
    }

    [Fact]
    public async Task ProcessBatch_on_a_non_running_campaign_just_reports_progress()
    {
        Assert.Null(await Sut().ProcessBatchAsync(_clinicId, Guid.NewGuid()));
        var scheduled = MakeCampaign(CampaignStatus.Scheduled);
        _campaigns.Setup(x => x.CountRecipientsInStatusAsync(scheduled.Id, CampaignRecipientStatus.Queued, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        var r = await Sut().ProcessBatchAsync(_clinicId, scheduled.Id);
        Assert.Equal((0, 3, false), (r!.Processed, r.RemainingQueued, r.CampaignCompleted));
        var done = MakeCampaign(CampaignStatus.Completed);
        Assert.True((await Sut().ProcessBatchAsync(_clinicId, done.Id))!.CampaignCompleted);
    }

    [Fact]
    public async Task A_failed_recipient_is_recorded_and_the_batch_continues()
    {
        var c = MakeCampaign(CampaignStatus.Running);
        var bad = Recipient(c, CampaignRecipientStatus.Queued);
        var good = Recipient(c, CampaignRecipientStatus.Queued);
        _campaigns.Setup(x => x.ListQueuedBatchAsync(c.Id, 5, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { bad, good });
        _campaigns.Setup(x => x.CountRecipientsInStatusAsync(c.Id, CampaignRecipientStatus.Queued, It.IsAny<CancellationToken>())).ReturnsAsync(4);
        var conv = new ConversationResponse(Guid.NewGuid(), _clinicId, Guid.NewGuid(), "whatsapp", "active", "ai", true, false, null, null, null, false, DateTimeOffset.UtcNow);
        _conversations.Setup(x => x.GetOrCreateForLeadAsync(_clinicId, bad.LeadId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("no window"));
        _conversations.Setup(x => x.GetOrCreateForLeadAsync(_clinicId, good.LeadId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(conv);

        var r = await Sut().ProcessBatchAsync(_clinicId, c.Id, 5);

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
        var conv = new ConversationResponse(Guid.NewGuid(), _clinicId, r.LeadId, "whatsapp", "active", "ai", true, false, null, null, null, false, DateTimeOffset.UtcNow);
        _conversations.Setup(x => x.GetOrCreateForLeadAsync(_clinicId, r.LeadId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(conv);
        await Sut().ProcessBatchAsync(_clinicId, c.Id);
        _messages.Verify(m => m.SendCampaignTemplateAsync(_clinicId, conv.Id, _template.Id, It.Is<IReadOnlyList<string>>(v => v.SequenceEqual(new[] { "Ann", "20%" })), c.Id, r.Id, It.IsAny<CancellationToken>()), Times.Once);
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
        var result = await Sut().ProcessBatchAsync(_clinicId, c.Id);
        Assert.Equal(1, result!.Failed);
        Assert.Contains("no WhatsApp template", r.FailureReason);
    }

    [Fact]
    public async Task Insufficient_funds_or_entitlement_errors_stop_the_batch_and_leave_the_recipient_queued()
    {
        var c = MakeCampaign(CampaignStatus.Running);
        var r = Recipient(c, CampaignRecipientStatus.Queued);
        _campaigns.Setup(x => x.ListQueuedBatchAsync(c.Id, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { r });
        _conversations.Setup(x => x.GetOrCreateForLeadAsync(_clinicId, r.LeadId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BillingDeniedException(UsageFailureReason.InsufficientFunds, "out of credit"));
        await Assert.ThrowsAsync<BillingDeniedException>(() => Sut().ProcessBatchAsync(_clinicId, c.Id));
        Assert.Equal(CampaignRecipientStatus.Queued, r.Status);

        _conversations.Setup(x => x.GetOrCreateForLeadAsync(_clinicId, r.LeadId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new EntitlementDeniedException("messages", "plan"));
        await Assert.ThrowsAsync<EntitlementDeniedException>(() => Sut().ProcessBatchAsync(_clinicId, c.Id));
        Assert.Equal(CampaignRecipientStatus.Queued, r.Status);
    }

    [Fact]
    public async Task Other_billing_denials_fail_only_that_recipient()
    {
        var c = MakeCampaign(CampaignStatus.Running);
        var r = Recipient(c, CampaignRecipientStatus.Queued);
        _campaigns.Setup(x => x.ListQueuedBatchAsync(c.Id, 20, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { r });
        _conversations.Setup(x => x.GetOrCreateForLeadAsync(_clinicId, r.LeadId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BillingDeniedException(UsageFailureReason.RateNotFound, "no sub"));
        var result = await Sut().ProcessBatchAsync(_clinicId, c.Id);
        Assert.Equal(1, result!.Failed);
    }

    [Fact]
    public async Task Cancel_fails_outstanding_recipients_and_rejects_finished_campaigns()
    {
        Assert.Null(await Sut().CancelAsync(_clinicId, Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().CancelAsync(_clinicId, MakeCampaign(CampaignStatus.Completed).Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().CancelAsync(_clinicId, MakeCampaign(CampaignStatus.Cancelled).Id));

        var c = MakeCampaign(CampaignStatus.Running);
        var p = Recipient(c, CampaignRecipientStatus.Pending);
        var q = Recipient(c, CampaignRecipientStatus.Queued);
        _campaigns.Setup(x => x.ListRecipientsInStatusAsync(c.Id, It.Is<IReadOnlyCollection<string>>(s => s.Contains(CampaignRecipientStatus.Queued)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CampaignRecipient> { p, q });
        var r = await Sut().CancelAsync(_clinicId, c.Id);
        Assert.Equal(CampaignStatus.Cancelled, r!.Status);
        Assert.All(new[] { p, q }, x => Assert.Equal((CampaignRecipientStatus.Failed, "Campaign cancelled."), (x.Status, x.FailureReason)));
    }

    // ---------------------------------------------------------------- reads / stats

    [Fact]
    public async Task List_computes_per_campaign_stats_including_replies_after_sending()
    {
        _campaigns.Setup(r => r.ListWithTemplateAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Campaign>());
        Assert.Empty(await Sut().ListAsync(_clinicId));

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
        _campaigns.Setup(r => r.ListWithTemplateAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<Campaign> { c });
        _campaigns.Setup(r => r.ListRecipientsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(recipients);
        _messageRows.Setup(m => m.ListInboundCustomerMessageTimesAsync(_clinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, DateTimeOffset)> { (convId, sentAt.AddMinutes(5)), (recipients[1].ConversationId!.Value, sentAt.AddMinutes(-5)) });

        var row = Assert.Single(await Sut().ListAsync(_clinicId));

        Assert.Equal((6, 2, 2, 1, 1, 1), (row.TotalRecipients, row.Sent, row.Delivered, row.Failed, row.Read, row.Replied));
        Assert.Equal(("promo", true), (row.TemplateName, row.ManuallySelected));
    }

    [Fact]
    public async Task GetById_returns_details_with_stats_and_recipient_rows()
    {
        Assert.Null(await Sut().GetByIdAsync(_clinicId, Guid.NewGuid()));
        var c = MakeCampaign();
        var r = Recipient(c, CampaignRecipientStatus.Sent);
        r.Lead = new Lead { FullName = "Ann" };
        _campaigns.Setup(x => x.ListRecipientsWithLeadAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<CampaignRecipient> { r });
        var d = await Sut().GetByIdAsync(_clinicId, c.Id);
        Assert.Equal((1, 1, "Hi {{1}}", "Ann"), (d!.Stats.TotalRecipients, d.Stats.Sent, d.TemplateBody, d.Recipients[0].LeadFullName));
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
        Assert.Equal(expected, Sut().GetSkipReasonIfNotContactable(new Lead { Phone = phone, MarketingOptIn = optIn, OptedOutAt = optedOut ? DateTimeOffset.UtcNow : null }));

    [Fact]
    public async Task Eligible_leads_and_count_pass_parsed_filters_and_the_configured_default()
    {
        _config.SetupGet(c => c.CampaignsDefaultInactiveDays).Returns(45);
        var clinic = Guid.NewGuid();
        await Sut().GetEligibleLeadsAsync(clinic, CampaignAudienceType.AllEligible, "{\"inactiveDays\":10,\"cities\":[\"Beirut\"]}");
        _repo.Verify(r => r.ListEligibleLeadsAsync(clinic, CampaignAudienceType.AllEligible,
            It.Is<CampaignAudienceFilters>(f => f.InactiveDays == 10 && f.Cities!.Single() == "Beirut"), 45, It.IsAny<CancellationToken>()), Times.Once);
        await Sut().GetMatchingCountAsync(clinic, CampaignAudienceType.Custom, null);
        _repo.Verify(r => r.CountEligibleLeadsAsync(clinic, CampaignAudienceType.Custom, It.Is<CampaignAudienceFilters>(f => f.InactiveDays == null), 45, It.IsAny<CancellationToken>()), Times.Once);
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
        var f = CampaignAudienceFilters.Parse("{\"InactiveDays\":3,\"leadStatuses\":[\"new\"],\"createdAfter\":\"2026-01-01T00:00:00Z\"}");
        Assert.Equal((3, "new"), (f.InactiveDays, f.LeadStatuses!.Single()));
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), f.CreatedAfter);
    }
}
