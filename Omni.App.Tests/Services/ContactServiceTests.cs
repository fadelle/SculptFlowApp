using Omni.App.Business.Services.Contacts;

namespace Omni.App.Tests.Services;

public class ContactServiceTests
{
    private readonly Guid _clientId = Guid.NewGuid();
    private readonly Mock<IContactRepository> _contacts = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IEventLogger> _events = new();
    private readonly Mock<ICatalogService> _services = new();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IBusinessTypeService> _businessTypes = TextKit.PermissiveBusinessTypes();
    private readonly Mock<IMessageTracer> _tracer = new();

    public ContactServiceTests() =>
        _clients.Setup(c => c.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client { Id = _clientId, Name = "Test" });

    private ContactService Sut() => new(_contacts.Object, _uow.Object, _events.Object, _services.Object, _clients.Object, _businessTypes.Object, _tracer.Object);

    private Contact Existing(string stage = PipelineKit.New) => new()
    {
        Id = Guid.NewGuid(), ClientId = _clientId, FullName = "Old Name", StageKey = stage, Qualification = ContactQualification.Unknown,
        City = "Beirut", Phone = "961700"
    };

    private Contact Found(Contact? contact = null)
    {
        contact ??= Existing();
        _contacts.Setup(l => l.GetAsync(_clientId, contact.Id, It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        return contact;
    }

    private CreateContactRequest Create(string? externalId = null, Guid? serviceId = null) =>
        new(_clientId, serviceId, "Ann Lee", "Ann", "Lee", "961711", "a@x.com", "instagram", "story", "spring", externalId, "en", "Beirut", "asap", "notes", false);

    [Fact]
    public async Task Create_returns_the_existing_contact_for_a_known_external_id()
    {
        var contact = Existing();
        _contacts.Setup(l => l.FindByExternalIdAsync(_clientId, "ext1", It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        var (r, created) = await Sut().CreateOrGetAsync(Create("ext1"));
        Assert.False(created);
        Assert.Equal(contact.Id, r.Id);
        _contacts.Verify(l => l.Add(It.IsAny<Contact>()), Times.Never);
    }

    [Fact]
    public async Task Create_builds_a_new_contact_logs_the_event_and_loads_the_service()
    {
        Contact? added = null;
        _contacts.Setup(l => l.Add(It.IsAny<Contact>())).Callback<Contact>(l => added = l);
        var svc = Guid.NewGuid();
        var (r, created) = await Sut().CreateOrGetAsync(Create("ext2", svc));
        Assert.True(created);
        Assert.Equal((PipelineKit.New, ContactQualification.Unknown, false), (added!.StageKey, added.Qualification, added.MarketingOptIn));
        Assert.Equal(("Ann Lee", "instagram", "spring", "ext2"), (added.FullName, added.Source, added.CampaignName, added.ExternalContactId));
        _events.Verify(e => e.Log(_clientId, EventTypes.ContactCreated, added.Id, null, null, "instagram", "{}"), Times.Once);
        _contacts.Verify(l => l.LoadServiceAsync(added, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(added.Id, r.Id);
    }

    [Fact]
    public async Task Create_without_external_id_or_service_skips_lookup_and_service_load()
    {
        await Sut().CreateOrGetAsync(Create(null, null));
        _contacts.Verify(l => l.FindByExternalIdAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _contacts.Verify(l => l.LoadServiceAsync(It.IsAny<Contact>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_for_an_unknown_client_is_not_found_and_saves_nothing()
    {
        var request = Create("ext3") with { ClientId = Guid.NewGuid() };
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().CreateOrGetAsync(request));
        _contacts.Verify(l => l.Add(It.IsAny<Contact>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_without_a_name_phone_or_email_is_rejected()
    {
        var request = Create(null) with { FullName = " ", FirstName = null, LastName = "", Phone = null, Email = null };
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateOrGetAsync(request));
        _contacts.Verify(l => l.Add(It.IsAny<Contact>()), Times.Never);
    }

    [Theory]
    [InlineData("Ann", null, null)]
    [InlineData(null, "961711", null)]
    [InlineData(null, null, "a@x.com")]
    public async Task Create_needs_only_one_of_name_phone_or_email(string? first, string? phone, string? email)
    {
        var request = Create(null) with { FullName = null, FirstName = first, LastName = null, Phone = phone, Email = email };
        var (_, created) = await Sut().CreateOrGetAsync(request);
        Assert.True(created);
    }

    [Fact]
    public async Task GetById_and_List_map_results()
    {
        var contact = Found();
        Assert.Equal(contact.Id, (await Sut().GetByIdAsync(_clientId, contact.Id))!.Id);
        Assert.Null(await Sut().GetByIdAsync(_clientId, Guid.NewGuid()));
        _contacts.Setup(l => l.ListAsync(_clientId, "new", null, "ann", 5, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Contact> { contact } as IReadOnlyList<Contact>, 42));
        var (items, total) = await Sut().ListAsync(_clientId, "new", null, "ann", 5, 10);
        Assert.Equal((1, 42), (items.Count, total));
        _contacts.Setup(l => l.ListDistinctSourcesAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "a", "b" });
        Assert.Equal(["a", "b"], await Sut().GetDistinctSourcesAsync(_clientId));
    }

    [Fact]
    public async Task Update_changes_only_supplied_fields_and_validates_a_changed_service()
    {
        var contact = Found();
        var svc = Guid.NewGuid();
        var r = await Sut().UpdateAsync(_clientId, contact.Id, new UpdateContactRequest(svc, "New Name", null, null, null, "e@x.com", null, null, null, "n", null));
        Assert.Equal(("New Name", "Beirut", "961700", "e@x.com", "n", svc), (contact.FullName, contact.City, contact.Phone, contact.Email, contact.Notes, contact.ServiceId));
        Assert.Equal("New Name", r!.FullName);
        _services.Verify(p => p.EnsureUsableAsync(_clientId, svc, It.IsAny<CancellationToken>()), Times.Once);
        _contacts.Verify(l => l.LoadServiceAsync(contact, It.IsAny<CancellationToken>()), Times.Once);

        // resubmitting the current service is not re-validated (it may be inactive by now)
        await Sut().UpdateAsync(_clientId, contact.Id, new UpdateContactRequest(svc, null, null, null, null, null, null, null, null, null, null));
        _services.Verify(p => p.EnsureUsableAsync(_clientId, svc, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_returns_null_for_unknown_contact_and_propagates_service_errors()
    {
        Assert.Null(await Sut().UpdateAsync(_clientId, Guid.NewGuid(), new UpdateContactRequest(null, "x", null, null, null, null, null, null, null, null, null)));
        var contact = Found();
        _services.Setup(p => p.EnsureUsableAsync(_clientId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("inactive"));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAsync(_clientId, contact.Id, new UpdateContactRequest(Guid.NewGuid(), null, null, null, null, null, null, null, null, null, null)));
        Assert.Equal("Old Name", contact.FullName);
    }

    [Theory]
    [InlineData(PipelineKit.Qualified, EventTypes.ContactStageChanged)]
    [InlineData(PipelineKit.Booked, EventTypes.BookingCreated)]
    [InlineData(PipelineKit.Attended, EventTypes.BookingAttended)]
    [InlineData(PipelineKit.NoShow, EventTypes.BookingNoShow)]
    [InlineData(PipelineKit.NeedsHuman, EventTypes.HumanHandoff)]
    [InlineData(PipelineKit.Contacted, EventTypes.ContactStageChanged)]
    [InlineData(PipelineKit.Customer, EventTypes.ContactStageChanged)]
    [InlineData(PipelineKit.Lost, EventTypes.ContactStageChanged)]
    public async Task UpdateStage_logs_the_event_of_the_new_stages_category_and_stamps_last_contact(string stage, string eventType)
    {
        var contact = Found();
        var r = await Sut().UpdateStageAsync(_clientId, contact.Id, new UpdateContactStageRequest(stage, ContactQualification.Hot, "  referral "));
        Assert.Equal((stage, ContactQualification.Hot, "  referral "), (r!.StageKey, r.Qualification, contact.Source));
        Assert.NotNull(contact.LastContactAt);
        _events.Verify(e => e.Log(_clientId, eventType, contact.Id, null, null, null, $"{{\"from\":\"new\",\"to\":\"{stage}\"}}"), Times.Once);
    }

    [Fact]
    public async Task UpdateStage_follows_the_category_whatever_the_type_calls_the_stage()
    {
        _businessTypes.Use(PipelineKit.Custom());
        var contact = Found(Existing("inquiry"));
        await Sut().UpdateStageAsync(_clientId, contact.Id, new UpdateContactStageRequest("expert", null));
        Assert.Equal("expert", contact.StageKey);
        _events.Verify(e => e.Log(_clientId, EventTypes.HumanHandoff, contact.Id, null, null, null, It.IsAny<string>()), Times.Once);
        // a key of another business type is refused
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateStageAsync(_clientId, contact.Id, new UpdateContactStageRequest(PipelineKit.Booked, null)));
    }

    [Fact]
    public async Task UpdateStage_with_unchanged_stage_does_not_count_as_contact()
    {
        var contact = Found(Existing(PipelineKit.Qualified));
        await Sut().UpdateStageAsync(_clientId, contact.Id, new UpdateContactStageRequest(PipelineKit.Qualified, ContactQualification.Warm));
        Assert.Null(contact.LastContactAt);
        Assert.Equal(ContactQualification.Warm, contact.Qualification);
        _events.Verify(e => e.Log(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateStage_validates_input_and_missing_contact()
    {
        var unknown = await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateStageAsync(_clientId, Guid.NewGuid(), new UpdateContactStageRequest("bogus", null)));
        Assert.Equal("contacts.errors.unknownStage", unknown.Message);
        Assert.Equal("'bogus' isn't one of your contact stages.", TextKit.Localizer().Localize(unknown));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateStageAsync(_clientId, Guid.NewGuid(), new UpdateContactStageRequest(PipelineKit.New, "bogus")));
        // medical_question is no longer a qualification (it became a needs-human stage)
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateStageAsync(_clientId, Guid.NewGuid(), new UpdateContactStageRequest(PipelineKit.New, "medical_question")));
        Assert.Null(await Sut().UpdateStageAsync(_clientId, Guid.NewGuid(), new UpdateContactStageRequest(PipelineKit.New, null)));
    }

    [Fact]
    public async Task ListStages_returns_the_clients_business_type_stages()
    {
        IReadOnlyList<PipelineStageOption> stages = [new("new", StageCategory.Open, "New"), new("booked", StageCategory.Booked, "Booked")];
        _businessTypes.Setup(t => t.ListStagesForClientAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(stages);
        Assert.Same(stages, await Sut().ListStagesAsync(_clientId));
    }

    [Fact]
    public async Task UpdateContext_applies_ai_collected_details()
    {
        var contact = Found();
        var follow = DateTimeOffset.UtcNow.AddDays(2);
        var svc = Guid.NewGuid();
        var r = await Sut().UpdateContextAsync(_clientId, contact.Id, new UpdateContactContextRequest(svc, "ar", "Tripoli", "soon", "likes X", follow, ContactQualification.Hot));
        Assert.Equal(("ar", "Tripoli", "soon", "likes X", follow, ContactQualification.Hot, svc),
            (contact.PreferredLanguage, contact.City, contact.DesiredTimeline, contact.Notes, contact.NextFollowupAt, contact.Qualification, contact.ServiceId));
        Assert.NotNull(r);
        _services.Verify(p => p.EnsureUsableAsync(_clientId, svc, It.IsAny<CancellationToken>()), Times.Once);

        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateContextAsync(_clientId, contact.Id, new UpdateContactContextRequest(null, null, null, null, null, null, "bogus")));
        Assert.Null(await Sut().UpdateContextAsync(_clientId, Guid.NewGuid(), new UpdateContactContextRequest(null, null, null, null, null, null, null)));
        Assert.Equal(PipelineKit.New, contact.StageKey); // no stage given: the stage stays
    }

    [Fact]
    public async Task UpdateContext_stores_the_ais_language_as_a_code_and_ignores_text_that_is_no_language()
    {
        var contact = Found();
        contact.PreferredLanguage = "en";

        await Sut().UpdateContextAsync(_clientId, contact.Id, new UpdateContactContextRequest(null, "Lebanese", null, null, null, null, null));
        Assert.Equal("ar", contact.PreferredLanguage);
        _tracer.Verify(t => t.Step(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Omni.App.Entities.Dtos.Monitoring.TraceIds>(),
            It.IsAny<double?>(), It.IsAny<object?>()), Times.Never);

        await Sut().UpdateContextAsync(_clientId, contact.Id, new UpdateContactContextRequest(null, "  ", null, null, null, null, null));
        Assert.Equal("ar", contact.PreferredLanguage);                    // blank from the AI never erases a known language

        await Sut().UpdateContextAsync(_clientId, contact.Id, new UpdateContactContextRequest(null, "the contact's own dialect", null, null, null, null, null));
        Assert.Equal("ar", contact.PreferredLanguage);
        _tracer.Verify(t => t.Step(TraceSteps.ContactLanguageIgnored, TraceOutcomes.Skipped,
            It.Is<Omni.App.Entities.Dtos.Monitoring.TraceIds>(i => i.ClientId == _clientId), null, It.IsAny<object?>()), Times.Once);
    }

    [Fact]
    public async Task The_dashboard_stores_a_language_as_its_code_clears_a_blank_and_refuses_text_that_is_no_language()
    {
        Contact? added = null;
        _contacts.Setup(l => l.Add(It.IsAny<Contact>())).Callback<Contact>(l => added = l);
        await Sut().CreateOrGetAsync(Create() with { PreferredLanguage = " French " });
        Assert.Equal("fr", added!.PreferredLanguage);
        var refused = await Assert.ThrowsAsync<ArgumentException>(() => Sut().CreateOrGetAsync(Create() with { PreferredLanguage = "Klingon" }));
        Assert.Equal(("contacts.errors.invalidLanguage", "'Klingon' isn't a language. Use a two-letter code such as es."),
            (refused.Message, TextKit.English(refused)));

        var contact = Found();
        await Sut().UpdateAsync(_clientId, contact.Id, new UpdateContactRequest(null, null, null, null, null, null, "es-MX", null, null, null, null));
        Assert.Equal("es", contact.PreferredLanguage);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Sut().UpdateAsync(_clientId, contact.Id, new UpdateContactRequest(null, null, null, null, null, null, "xx", null, null, null, null)));
        Assert.Equal("es", contact.PreferredLanguage);

        await Sut().UpdateStageAsync(_clientId, contact.Id, new UpdateContactStageRequest(PipelineKit.New, null, null, "arabic"));
        Assert.Equal("ar", contact.PreferredLanguage);
        await Sut().UpdateStageAsync(_clientId, contact.Id, new UpdateContactStageRequest(PipelineKit.New, null));
        Assert.Equal("ar", contact.PreferredLanguage);                    // not sent: unchanged
        await Sut().UpdateStageAsync(_clientId, contact.Id, new UpdateContactStageRequest(PipelineKit.New, null, null, ""));
        Assert.Null(contact.PreferredLanguage);                           // blank: cleared
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Sut().UpdateStageAsync(_clientId, contact.Id, new UpdateContactStageRequest(PipelineKit.New, null, null, "zz")));
    }

    [Theory]
    [InlineData(StageCategory.Booked, "visit_set")]   // a category: the type's first stage of it
    [InlineData("needs_human", "team")]
    [InlineData("expert", "expert")]                   // a stage key of the type
    [InlineData(" signed ", "signed")]
    public async Task UpdateContext_moves_the_stage_by_category_or_key(string stage, string expected)
    {
        _businessTypes.Use(PipelineKit.Custom());
        var contact = Found(Existing("inquiry"));
        await Sut().UpdateContextAsync(_clientId, contact.Id, new UpdateContactContextRequest(null, null, null, null, null, null, null, stage));
        Assert.Equal(expected, contact.StageKey);
        Assert.NotNull(contact.LastContactAt);
        _events.Verify(e => e.Log(_clientId, It.IsAny<string>(), contact.Id, null, null, null, $"{{\"from\":\"inquiry\",\"to\":\"{expected}\"}}"), Times.Once);
    }

    [Fact]
    public async Task UpdateContext_refuses_a_stage_that_is_neither_a_key_nor_a_category()
    {
        _businessTypes.Use(PipelineKit.Custom());
        var contact = Found(Existing("inquiry"));
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Sut().UpdateContextAsync(_clientId, contact.Id, new UpdateContactContextRequest(null, null, null, null, null, null, null, PipelineKit.Customer)));
        Assert.Equal("contacts.errors.unknownStage", ex.Message);
        Assert.Equal("inquiry", contact.StageKey);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task New_contacts_start_at_the_first_open_stage_of_their_type()
    {
        _businessTypes.Use(PipelineKit.Custom());
        _uow.Setup(u => u.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var added = new List<Contact>();
        _contacts.Setup(l => l.Add(It.IsAny<Contact>())).Callback<Contact>(added.Add);
        await Sut().CreateOrGetAsync(Create("ext9"));
        await Sut().GetOrCreateByPhoneAsync(_clientId, "961798", "P");
        await Sut().GetOrCreateByExternalIdAsync(_clientId, "telegram:1", "telegram", "T", null, null, null);
        Assert.Equal(["inquiry", "inquiry", "inquiry"], added.Select(c => c.StageKey));
    }

    [Fact]
    public async Task GetOrCreateByPhone_finds_or_creates_a_whatsapp_contact()
    {
        var contact = Existing();
        _contacts.Setup(l => l.FindByPhoneAsync(_clientId, "961700", It.IsAny<CancellationToken>())).ReturnsAsync(contact);
        var (found, wasCreated) = await Sut().GetOrCreateByPhoneAsync(_clientId, "961700", "X");
        Assert.False(wasCreated);
        Assert.Equal(contact.Id, found.Id);

        Contact? added = null;
        _contacts.Setup(l => l.Add(It.IsAny<Contact>())).Callback<Contact>(l => added = l);
        var (fresh, created) = await Sut().GetOrCreateByPhoneAsync(_clientId, "961799", "New Person");
        Assert.True(created);
        Assert.Equal(("whatsapp", "whatsapp:961799", true), (added!.Source, added.ExternalContactId, added.MarketingOptIn));
        _events.Verify(e => e.Log(_clientId, EventTypes.ContactCreated, added.Id, null, null, "whatsapp", "{}"), Times.Once);
        Assert.Equal(added.Id, fresh.Id);
    }

    [Fact]
    public async Task GetOrCreateByExternalId_creates_returns_existing_or_yields_to_the_concurrent_winner()
    {
        var winner = Existing();
        _contacts.SetupSequence(l => l.FindByExternalIdAsync(_clientId, "ig:1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Contact?)null)   // first look-up: nobody yet
            .ReturnsAsync(winner);       // after the conflict
        _uow.Setup(u => u.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var (contact, created) = await Sut().GetOrCreateByExternalIdAsync(_clientId, "ig:1", "instagram", "Z", "Z", null, "dm");
        Assert.False(created);
        Assert.Equal(winner.Id, contact.Id);

        _uow.Setup(u => u.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _contacts.Setup(l => l.FindByExternalIdAsync(_clientId, "ig:2", It.IsAny<CancellationToken>())).ReturnsAsync((Contact?)null);
        Contact? added = null;
        _contacts.Setup(l => l.Add(It.IsAny<Contact>())).Callback<Contact>(l => added = l);
        var (fresh, createdNew) = await Sut().GetOrCreateByExternalIdAsync(_clientId, "ig:2", "instagram", "Y", null, null, null);
        Assert.True(createdNew);
        Assert.Null(added!.Phone);
        Assert.Equal(added.Id, fresh.Id);

        _contacts.Setup(l => l.FindByExternalIdAsync(_clientId, "ig:3", It.IsAny<CancellationToken>())).ReturnsAsync(winner);
        Assert.False((await Sut().GetOrCreateByExternalIdAsync(_clientId, "ig:3", "instagram", null, null, null, null)).WasCreated);
    }

    [Fact]
    public async Task Conflict_with_no_winner_found_is_an_error()
    {
        _contacts.Setup(l => l.FindByExternalIdAsync(_clientId, "ig:9", It.IsAny<CancellationToken>())).ReturnsAsync((Contact?)null);
        _uow.Setup(u => u.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().GetOrCreateByExternalIdAsync(_clientId, "ig:9", "instagram", null, null, null, null));
    }
}
