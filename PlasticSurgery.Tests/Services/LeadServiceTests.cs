using PlasticSurgery.Business.Services.Leads;

namespace PlasticSurgery.Tests.Services;

public class LeadServiceTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<ILeadRepository> _leads = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IEventLogger> _events = new();
    private readonly Mock<IProcedureService> _procedures = new();

    private LeadService Sut() => new(_leads.Object, _uow.Object, _events.Object, _procedures.Object);

    private Lead Existing(string status = LeadStatus.New) => new()
    {
        Id = Guid.NewGuid(), ClinicId = _clinicId, FullName = "Old Name", Status = status, QualificationStatus = LeadQualificationStatus.Unknown,
        City = "Beirut", Phone = "961700"
    };

    private Lead Found(Lead? lead = null)
    {
        lead ??= Existing();
        _leads.Setup(l => l.GetAsync(_clinicId, lead.Id, It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        return lead;
    }

    private CreateLeadRequest Create(string? externalId = null, Guid? procedureId = null) =>
        new(_clinicId, procedureId, "Ann Lee", "Ann", "Lee", "961711", "a@x.com", "instagram", "story", "spring", externalId, "en", "Beirut", "asap", "notes", false);

    [Fact]
    public async Task Create_returns_the_existing_lead_for_a_known_external_id()
    {
        var lead = Existing();
        _leads.Setup(l => l.FindByExternalIdAsync(_clinicId, "ext1", It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        var (r, created) = await Sut().CreateOrGetAsync(Create("ext1"));
        Assert.False(created);
        Assert.Equal(lead.Id, r.Id);
        _leads.Verify(l => l.Add(It.IsAny<Lead>()), Times.Never);
    }

    [Fact]
    public async Task Create_builds_a_new_lead_logs_the_event_and_loads_the_procedure()
    {
        Lead? added = null;
        _leads.Setup(l => l.Add(It.IsAny<Lead>())).Callback<Lead>(l => added = l);
        var proc = Guid.NewGuid();
        var (r, created) = await Sut().CreateOrGetAsync(Create("ext2", proc));
        Assert.True(created);
        Assert.Equal((LeadStatus.New, LeadQualificationStatus.Unknown, false), (added!.Status, added.QualificationStatus, added.MarketingOptIn));
        Assert.Equal(("Ann Lee", "instagram", "spring", "ext2"), (added.FullName, added.Source, added.CampaignName, added.ExternalLeadId));
        _events.Verify(e => e.Log(_clinicId, EventTypes.LeadCreated, added.Id, null, null, "instagram", "{}"), Times.Once);
        _leads.Verify(l => l.LoadProcedureAsync(added, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(added.Id, r.Id);
    }

    [Fact]
    public async Task Create_without_external_id_or_procedure_skips_lookup_and_procedure_load()
    {
        await Sut().CreateOrGetAsync(Create(null, null));
        _leads.Verify(l => l.FindByExternalIdAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _leads.Verify(l => l.LoadProcedureAsync(It.IsAny<Lead>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetById_and_List_map_results()
    {
        var lead = Found();
        Assert.Equal(lead.Id, (await Sut().GetByIdAsync(_clinicId, lead.Id))!.Id);
        Assert.Null(await Sut().GetByIdAsync(_clinicId, Guid.NewGuid()));
        _leads.Setup(l => l.ListAsync(_clinicId, "new", null, "ann", 5, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Lead> { lead } as IReadOnlyList<Lead>, 42));
        var (items, total) = await Sut().ListAsync(_clinicId, "new", null, "ann", 5, 10);
        Assert.Equal((1, 42), (items.Count, total));
        _leads.Setup(l => l.ListDistinctSourcesAsync(_clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "a", "b" });
        Assert.Equal(["a", "b"], await Sut().GetDistinctSourcesAsync(_clinicId));
    }

    [Fact]
    public async Task Update_changes_only_supplied_fields_and_validates_a_changed_procedure()
    {
        var lead = Found();
        var proc = Guid.NewGuid();
        var r = await Sut().UpdateAsync(_clinicId, lead.Id, new UpdateLeadRequest(proc, "New Name", null, null, null, "e@x.com", null, null, null, "n", null));
        Assert.Equal(("New Name", "Beirut", "961700", "e@x.com", "n", proc), (lead.FullName, lead.City, lead.Phone, lead.Email, lead.Notes, lead.ProcedureId));
        Assert.Equal("New Name", r!.FullName);
        _procedures.Verify(p => p.EnsureUsableAsync(_clinicId, proc, It.IsAny<CancellationToken>()), Times.Once);
        _leads.Verify(l => l.LoadProcedureAsync(lead, It.IsAny<CancellationToken>()), Times.Once);

        // resubmitting the current procedure is not re-validated (it may be inactive by now)
        await Sut().UpdateAsync(_clinicId, lead.Id, new UpdateLeadRequest(proc, null, null, null, null, null, null, null, null, null, null));
        _procedures.Verify(p => p.EnsureUsableAsync(_clinicId, proc, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_returns_null_for_unknown_lead_and_propagates_procedure_errors()
    {
        Assert.Null(await Sut().UpdateAsync(_clinicId, Guid.NewGuid(), new UpdateLeadRequest(null, "x", null, null, null, null, null, null, null, null, null)));
        var lead = Found();
        _procedures.Setup(p => p.EnsureUsableAsync(_clinicId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("inactive"));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateAsync(_clinicId, lead.Id, new UpdateLeadRequest(Guid.NewGuid(), null, null, null, null, null, null, null, null, null, null)));
        Assert.Equal("Old Name", lead.FullName);
    }

    [Theory]
    [InlineData(LeadStatus.Qualified, EventTypes.LeadQualified)]
    [InlineData(LeadStatus.ConsultationBooked, EventTypes.ConsultationBooked)]
    [InlineData(LeadStatus.ConsultationAttended, EventTypes.ConsultationAttended)]
    [InlineData(LeadStatus.NoShow, EventTypes.ConsultationNoShow)]
    [InlineData(LeadStatus.NeedsHuman, EventTypes.HumanHandoff)]
    [InlineData(LeadStatus.Contacted, EventTypes.LeadContacted)]
    [InlineData(LeadStatus.Lost, EventTypes.LeadContacted)]
    public async Task UpdateStatus_logs_the_matching_event_and_stamps_last_contact(string status, string eventType)
    {
        var lead = Found();
        var r = await Sut().UpdateStatusAsync(_clinicId, lead.Id, new UpdateLeadStatusRequest(status, LeadQualificationStatus.Hot, "  referral "));
        Assert.Equal((status, LeadQualificationStatus.Hot, "  referral "), (r!.Status, r.QualificationStatus, lead.Source));
        Assert.NotNull(lead.LastContactAt);
        _events.Verify(e => e.Log(_clinicId, eventType, lead.Id, null, null, null, "{}"), Times.Once);
    }

    [Fact]
    public async Task UpdateStatus_with_unchanged_status_does_not_count_as_contact()
    {
        var lead = Found(Existing(LeadStatus.Qualified));
        await Sut().UpdateStatusAsync(_clinicId, lead.Id, new UpdateLeadStatusRequest(LeadStatus.Qualified, LeadQualificationStatus.Warm));
        Assert.Null(lead.LastContactAt);
        Assert.Equal(LeadQualificationStatus.Warm, lead.QualificationStatus);
        _events.Verify(e => e.Log(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UpdateStatus_validates_input_and_missing_lead()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateStatusAsync(_clinicId, Guid.NewGuid(), new UpdateLeadStatusRequest("bogus", null)));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateStatusAsync(_clinicId, Guid.NewGuid(), new UpdateLeadStatusRequest(LeadStatus.New, "bogus")));
        Assert.Null(await Sut().UpdateStatusAsync(_clinicId, Guid.NewGuid(), new UpdateLeadStatusRequest(LeadStatus.New, null)));
    }

    [Fact]
    public async Task UpdateContext_applies_ai_collected_details()
    {
        var lead = Found();
        var follow = DateTimeOffset.UtcNow.AddDays(2);
        var proc = Guid.NewGuid();
        var r = await Sut().UpdateContextAsync(_clinicId, lead.Id, new UpdateLeadContextRequest(proc, "ar", "Tripoli", "soon", "likes X", follow, LeadQualificationStatus.Hot));
        Assert.Equal(("ar", "Tripoli", "soon", "likes X", follow, LeadQualificationStatus.Hot, proc),
            (lead.PreferredLanguage, lead.City, lead.DesiredTimeline, lead.Notes, lead.NextFollowupAt, lead.QualificationStatus, lead.ProcedureId));
        Assert.NotNull(r);
        _procedures.Verify(p => p.EnsureUsableAsync(_clinicId, proc, It.IsAny<CancellationToken>()), Times.Once);

        await Assert.ThrowsAsync<ArgumentException>(() => Sut().UpdateContextAsync(_clinicId, lead.Id, new UpdateLeadContextRequest(null, null, null, null, null, null, "bogus")));
        Assert.Null(await Sut().UpdateContextAsync(_clinicId, Guid.NewGuid(), new UpdateLeadContextRequest(null, null, null, null, null, null, null)));
    }

    [Fact]
    public async Task GetOrCreateByPhone_finds_or_creates_a_whatsapp_lead()
    {
        var lead = Existing();
        _leads.Setup(l => l.FindByPhoneAsync(_clinicId, "961700", It.IsAny<CancellationToken>())).ReturnsAsync(lead);
        var (found, wasCreated) = await Sut().GetOrCreateByPhoneAsync(_clinicId, "961700", "X");
        Assert.False(wasCreated);
        Assert.Equal(lead.Id, found.Id);

        Lead? added = null;
        _leads.Setup(l => l.Add(It.IsAny<Lead>())).Callback<Lead>(l => added = l);
        var (fresh, created) = await Sut().GetOrCreateByPhoneAsync(_clinicId, "961799", "New Person");
        Assert.True(created);
        Assert.Equal(("whatsapp", "whatsapp:961799", true), (added!.Source, added.ExternalLeadId, added.MarketingOptIn));
        _events.Verify(e => e.Log(_clinicId, EventTypes.LeadCreated, added.Id, null, null, "whatsapp", "{}"), Times.Once);
        Assert.Equal(added.Id, fresh.Id);
    }

    [Fact]
    public async Task GetOrCreateByExternalId_creates_returns_existing_or_yields_to_the_concurrent_winner()
    {
        var winner = Existing();
        _leads.SetupSequence(l => l.FindByExternalIdAsync(_clinicId, "ig:1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Lead?)null)   // first look-up: nobody yet
            .ReturnsAsync(winner);       // after the conflict
        _uow.Setup(u => u.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var (lead, created) = await Sut().GetOrCreateByExternalIdAsync(_clinicId, "ig:1", "instagram", "Z", "Z", null, "dm");
        Assert.False(created);
        Assert.Equal(winner.Id, lead.Id);

        _uow.Setup(u => u.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _leads.Setup(l => l.FindByExternalIdAsync(_clinicId, "ig:2", It.IsAny<CancellationToken>())).ReturnsAsync((Lead?)null);
        Lead? added = null;
        _leads.Setup(l => l.Add(It.IsAny<Lead>())).Callback<Lead>(l => added = l);
        var (fresh, createdNew) = await Sut().GetOrCreateByExternalIdAsync(_clinicId, "ig:2", "instagram", "Y", null, null, null);
        Assert.True(createdNew);
        Assert.Null(added!.Phone);
        Assert.Equal(added.Id, fresh.Id);

        _leads.Setup(l => l.FindByExternalIdAsync(_clinicId, "ig:3", It.IsAny<CancellationToken>())).ReturnsAsync(winner);
        Assert.False((await Sut().GetOrCreateByExternalIdAsync(_clinicId, "ig:3", "instagram", null, null, null, null)).WasCreated);
    }

    [Fact]
    public async Task Conflict_with_no_winner_found_is_an_error()
    {
        _leads.Setup(l => l.FindByExternalIdAsync(_clinicId, "ig:9", It.IsAny<CancellationToken>())).ReturnsAsync((Lead?)null);
        _uow.Setup(u => u.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Sut().GetOrCreateByExternalIdAsync(_clinicId, "ig:9", "instagram", null, null, null, null));
    }
}
