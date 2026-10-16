using Omni.App.Business.Mappers.BusinessTypes;
using Omni.App.Business.Services.BusinessTypes;

namespace Omni.App.Tests.Services;

public class BusinessTypeServiceTests
{
    private readonly Mock<IBusinessTypeRepository> _types = new();
    private readonly Mock<IClientRepository> _clients = new();
    private readonly Mock<IContactRepository> _contacts = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IUnitOfWorkTransaction> _tx = new();
    private readonly List<BusinessType> _all = [];
    private readonly Guid _clientId = Guid.NewGuid();

    public BusinessTypeServiceTests()
    {
        _types.Setup(t => t.ListReadOnlyAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _all);
        _types.Setup(t => t.GetReadOnlyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => _all.FirstOrDefault(t => t.Code == code));
        _types.Setup(t => t.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => _all.FirstOrDefault(t => t.Code == code));
        _types.Setup(t => t.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => _all.Any(t => t.Code == code));
        _types.Setup(t => t.CountClientsByTypeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, int> { ["shop"] = 3 });
        _types.Setup(t => t.CountContactsByStageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, int>());
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_tx.Object);
        _all.Add(Type("shop", "Shop", ("new", "open"), ("contacted", "open"), ("booked", "booked"), ("came", "attended"), ("no_show", "no_show"),
            ("customer", "converted"), ("lost", "closed_lost"), ("needs_human", "needs_human")));
        _all.Add(Type("spa", "Spa", ("fresh", "open"), ("contacted", "open"), ("visit_booked", "booked"), ("visited", "attended"),
            ("missed", "no_show"), ("member", "converted"), ("gone", "closed_lost"), ("question", "needs_human"), ("vip_question", "needs_human")));
    }

    private BusinessTypeService Sut(string language = "en") =>
        new(_types.Object, _clients.Object, _contacts.Object, _uow.Object, TextKit.Context(language), TimeProvider.System);

    private static BusinessType Type(string code, string name, params (string Key, string Category)[] stages) => new()
    {
        Code = code,
        NameJson = $$"""{"en": "{{name}}", "fr": "{{name}} FR"}""",
        TerminologyJson = """{"en": {"contact": {"one": "guest", "other": "guests"}}}""",
        IsActive = true,
        Stages = stages.Select((s, i) => new PipelineStage
        {
            BusinessTypeCode = code, Key = s.Key, Category = s.Category, LabelsJson = $$"""{"en": "{{s.Key}} label"}""", SortOrder = (i + 1) * 10,
        }).ToList(),
    };

    private static SaveBusinessTypeRequest Request(string? code = "salon", List<SaveStageRequest>? stages = null) => new(
        code,
        new Dictionary<string, string> { ["en"] = " Salon ", ["fr"] = "Salon de beauté", ["ar"] = " " },
        new Dictionary<string, Dictionary<string, TermForms>>
        {
            ["en"] = TermName.All.ToDictionary(t => t, t => new TermForms(" " + t + " ", t + "s")),
            ["fr"] = new() { ["contact"] = new TermForms("cliente", "clientes"), ["order"] = new TermForms("commande", " ") },
        },
        "  Be kind.  ",
        30,
        stages ?? StageCategory.All.Select(c => new SaveStageRequest("s_" + c, c, new Dictionary<string, string> { ["en"] = "Stage " + c })).ToList());

    private static async Task<string> ErrorOf(Func<Task> act)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(act);
        return TextKit.Localizer().Localize(ex);
    }

    // ---- reads for the dashboard ---------------------------------------------------------------------------------

    [Fact]
    public async Task Options_list_active_types_in_the_request_language_falling_back_to_english()
    {
        _all[1].IsActive = false;
        Assert.Equal([new BusinessTypeOption("shop", "Shop FR")], await Sut("fr").ListOptionsAsync());
        Assert.Equal([new BusinessTypeOption("shop", "Shop")], await Sut("ar").ListOptionsAsync());
    }

    [Theory]
    [InlineData("shop", true)]
    [InlineData(" shop ", true)]
    [InlineData("spa", false)]
    [InlineData("nope", false)]
    [InlineData(" ", false)]
    [InlineData(null, false)]
    public async Task Only_existing_active_types_can_be_chosen(string? code, bool expected)
    {
        _all[1].IsActive = false;
        Assert.Equal(expected, await Sut().IsActiveAsync(code));
    }

    [Fact]
    public async Task A_clients_stages_come_from_its_business_type_in_pipeline_order()
    {
        _clients.Setup(c => c.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client { Id = _clientId, BusinessTypeCode = "spa" });

        var stages = await Sut().ListStagesForClientAsync(_clientId);
        Assert.Equal("fresh", stages[0].Key);
        Assert.Equal(new PipelineStageOption("visit_booked", "booked", "visit_booked label"), stages[2]);
        Assert.True(await Sut().IsStageOfClientAsync(_clientId, "vip_question"));
        Assert.False(await Sut().IsStageOfClientAsync(_clientId, "customer"));   // a stage of another type
        Assert.False(await Sut().IsStageOfClientAsync(_clientId, null));
        Assert.Empty(await Sut().ListStagesForClientAsync(Guid.NewGuid()));     // unknown client
    }

    // ---- admin reads ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Admin_list_and_get_include_every_language_and_the_client_count()
    {
        _all[1].IsActive = false;
        var list = await Sut().ListAsync();
        Assert.Equal(["shop", "spa"], list.Select(t => t.Code));
        Assert.Equal((3, 0), (list[0].ClientCount, list[1].ClientCount));
        Assert.Equal("Shop FR", list[0].Name["fr"]);
        Assert.Equal(new TermForms("guest", "guests"), list[0].Terminology["en"]["contact"]);
        Assert.False(list[1].IsActive);

        Assert.Equal(9, (await Sut().GetAsync("spa")).Stages.Count);
        Assert.Equal("There is no business type 'x'.", await ErrorOf(() => Sut().GetAsync("x")));
    }

    // ---- create / update / activate ------------------------------------------------------------------------------

    [Fact]
    public async Task Create_saves_a_cleaned_type_with_stages_in_list_order()
    {
        BusinessType? added = null;
        _types.Setup(t => t.Add(It.IsAny<BusinessType>())).Callback<BusinessType>(t => added = t);

        var created = await Sut().CreateAsync(Request(" salon "));

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(("salon", true, 30, "Be kind."), (added!.Code, added.IsActive, added.SortOrder, added.AiProfile));
        Assert.Equal(new Dictionary<string, string> { ["en"] = "Salon", ["fr"] = "Salon de beauté" }, created.Name);  // blank ar dropped
        Assert.Equal(new TermForms("contact", "contacts"), created.Terminology["en"]["contact"]);                      // trimmed
        Assert.Equal([new KeyValuePair<string, TermForms>("contact", new TermForms("cliente", "clientes"))], created.Terminology["fr"]); // half-filled word dropped
        Assert.Equal(StageCategory.All.Select(c => "s_" + c), created.Stages.Select(s => s.Key));
        Assert.Equal([10, 20, 30, 40, 50, 60, 70], created.Stages.Select(s => s.SortOrder));
        Assert.All(added.Stages, s => Assert.Equal("salon", s.BusinessTypeCode));
    }

    public static TheoryData<string?, SaveBusinessTypeRequest, string> InvalidRequests()
    {
        var ok = Request();
        var stages = ok.Stages!;
        List<SaveStageRequest> With(params SaveStageRequest[] extra) => [.. stages, .. extra];
        var data = new TheoryData<string?, SaveBusinessTypeRequest, string>
        {
            { "Salon", ok, "A code starts with a letter" },
            { "s", ok, "A code starts with a letter" },
            { "shop", ok, "A business type with the code 'shop' already exists." },
            { "salon", ok with { Name = new() { ["fr"] = "Salon" } }, "Give the business type an English name." },
            { "salon", ok with { Name = new() { ["de"] = "Salon" } }, "'de' isn't a language Omni supports." },
            { "salon", ok with { Name = new() { ["en"] = new string('n', 101) } }, "'name.en' is too long: at most 100 characters." },
            { "salon", ok with { Terminology = new() { ["en"] = new() { ["patient"] = new TermForms("a", "b") } } }, "'patient' isn't a word" }, // leftover-ok: an unknown term
            { "salon", ok with { Terminology = new() { ["en"] = new() { ["contact"] = new TermForms("a", "b") } } }, "Missing: member, service, booking, order, client." },
            { "salon", ok with { AiProfile = new string('a', 20001) } , "'aiProfile' is too long: at most 20000 characters." },
            { "salon", ok with { Stages = [] }, "Add at least one stage." },
            { "salon", ok with { Stages = Enumerable.Range(0, 51).Select(i => new SaveStageRequest("k" + i, "open", new() { ["en"] = "x" })).ToList() }, "at most 50 stages" },
            { "salon", ok with { Stages = With(new SaveStageRequest("Bad Key", "open", new() { ["en"] = "x" })) }, "The stage key 'Bad Key' must start" },
            { "salon", ok with { Stages = With(new SaveStageRequest("s_open", "open", new() { ["en"] = "x" })) }, "The stage key 's_open' is listed twice." },
            { "salon", ok with { Stages = With(new SaveStageRequest("won", "won", new() { ["en"] = "x" })) }, "The stage 'won' has an unknown category 'won'." },
            { "salon", ok with { Stages = With(new SaveStageRequest("x", "open", new() { ["fr"] = "x" })) }, "The stage 'x' needs an English label." },
            { "salon", ok with { Stages = stages.Where(s => s.Category != "no_show").ToList() }, "Every category needs at least one stage. Missing: no_show." },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Create_rejects_invalid_input_with_a_readable_message(string? code, SaveBusinessTypeRequest request, string message)
    {
        Assert.Contains(message, await ErrorOf(() => Sut().CreateAsync(request with { Code = code })));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_reports_a_taken_code_as_a_duplicate()
    {
        await Assert.ThrowsAsync<DuplicateRecordException>(() => Sut().CreateAsync(Request("shop")));
    }

    [Fact]
    public async Task Update_replaces_texts_keeps_unchanged_stage_rows_adds_new_ones_and_removes_unused_ones()
    {
        var spa = _all[1];
        var kept = spa.Stages.First(s => s.Key == "contacted");
        var request = Request(stages:
        [
            new("contacted", "open", new() { ["en"] = "Reached" }),
            new("visit_booked", "booked", new() { ["en"] = "Booked" }),
            new("visited", "attended", new() { ["en"] = "Visited" }),
            new("missed", "no_show", new() { ["en"] = "Missed" }),
            new("member", "converted", new() { ["en"] = "Member" }),
            new("gone", "closed_lost", new() { ["en"] = "Gone" }),
            new("question", "needs_human", new() { ["en"] = "Question" }),
            new("brand_new", "open", new() { ["en"] = "Brand new" }),
        ]);

        var updated = await Sut().UpdateAsync("spa", request);

        Assert.Same(kept, spa.Stages[0]);                                  // the tracked row is updated, not replaced
        Assert.Equal("""{"en":"Reached"}""", kept.LabelsJson);
        Assert.Equal("brand_new", spa.Stages[^1].Key);
        _types.Verify(t => t.RemoveStage(It.Is<PipelineStage>(s => s.Key == "fresh")), Times.Once);
        _types.Verify(t => t.RemoveStage(It.Is<PipelineStage>(s => s.Key == "vip_question")), Times.Once);
        Assert.Equal("Salon", BusinessTypeMapper.NameOf(spa, "en"));
        Assert.Equal("spa", updated.Code);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_refuses_to_remove_a_stage_that_still_has_contacts()
    {
        _types.Setup(t => t.CountContactsByStageAsync("spa", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { ["fresh"] = 2, ["vip_question"] = 1, ["contacted"] = 5 });
        var request = Request(stages: _all[1].Stages.Where(s => s.Key is not ("fresh" or "vip_question"))
            .Select(s => new SaveStageRequest(s.Key, s.Category, new() { ["en"] = s.Key })).Append(new SaveStageRequest("first", "open", new() { ["en"] = "First" })).ToList());

        var error = await ErrorOf(() => Sut().UpdateAsync("spa", request));

        Assert.Equal("These stages still have contacts in them, so they can't be removed: fresh, vip_question.", error);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("There is no business type 'nope'.", await ErrorOf(() => Sut().UpdateAsync("nope", Request())));
    }

    [Fact]
    public async Task Activation_never_switches_off_the_last_active_type()
    {
        await Sut().SetActiveAsync("spa", false);
        Assert.False(_all[1].IsActive);
        Assert.Equal("At least one business type must stay active, so new clients can sign up.", await ErrorOf(() => Sut().SetActiveAsync("shop", false)));
        await Sut().SetActiveAsync("shop", true);                                              // no change: nothing saved
        await Sut().SetActiveAsync("spa", true);
        Assert.True(_all[1].IsActive);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ---- moving a client to another type -------------------------------------------------------------------------

    [Fact]
    public void Moves_keep_a_matching_key_else_take_the_first_stage_of_the_same_category_else_the_first_open_stage()
    {
        var counts = new Dictionary<string, int>
        {
            ["fresh"] = 4, ["contacted"] = 3, ["visit_booked"] = 2, ["question"] = 1, ["vip_question"] = 1, ["legacy"] = 5, ["customer"] = 2, ["visited"] = 0,
        };

        var moves = BusinessTypeService.PlanMoves(_all[1].Stages, _all[0].Stages, counts);

        Assert.Equal(
        [
            ("fresh", "new", 4),                    // same category (open), first stage
            ("contacted", "contacted", 3),          // same key and category: stays
            ("visit_booked", "booked", 2),
            ("question", "needs_human", 1),
            ("vip_question", "needs_human", 1),
            ("customer", "customer", 2),            // unknown to the old type, but a stage of the new one
            ("legacy", "new", 5),                   // unknown everywhere: first open stage
        ], moves.Select(m => (m.FromKey, m.ToKey, m.Count)));
        Assert.Equal(("fresh label", "open", "new label"), (moves[0].FromLabel, moves[0].Category, moves[0].ToLabel));
        Assert.Equal(("legacy", ""), (moves[^1].FromLabel, moves[^1].Category));
    }

    [Fact]
    public async Task Preview_counts_contacts_and_changes_nothing()
    {
        _clients.Setup(c => c.GetReadOnlyAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client { Id = _clientId, BusinessTypeCode = "spa" });
        _contacts.Setup(l => l.CountByStageAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, int> { ["fresh"] = 4, ["member"] = 1 });

        var preview = await Sut().PreviewClientChangeAsync(_clientId, "shop");

        Assert.Equal((_clientId, "spa", "shop", 5), (preview.ClientId, preview.FromCode, preview.ToCode, preview.ContactCount));
        Assert.Equal([("fresh", "new"), ("member", "customer")], preview.Moves.Select(m => (m.FromKey, m.ToKey)));
        _contacts.Verify(l => l.MoveStageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("Client not found.", await ErrorOf(() => Sut().PreviewClientChangeAsync(Guid.NewGuid(), "shop")));
    }

    [Fact]
    public async Task Change_moves_contacts_and_the_client_in_one_transaction()
    {
        var client = new Client { Id = _clientId, BusinessTypeCode = "spa" };
        _clients.Setup(c => c.GetAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(client);
        _contacts.Setup(l => l.CountByStageAsync(_clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { ["fresh"] = 4, ["contacted"] = 2, ["member"] = 1 });

        var preview = await Sut().ChangeClientTypeAsync(_clientId, "shop");

        Assert.Equal(7, preview.ContactCount);
        _contacts.Verify(l => l.MoveStageAsync(_clientId, "fresh", "new", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _contacts.Verify(l => l.MoveStageAsync(_clientId, "member", "customer", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        _contacts.Verify(l => l.MoveStageAsync(_clientId, "contacted", It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal("shop", client.BusinessTypeCode);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Changing_to_the_current_type_changes_nothing()
    {
        _clients.Setup(c => c.GetAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client { Id = _clientId, BusinessTypeCode = "shop" });
        _contacts.Setup(l => l.CountByStageAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<string, int> { ["new"] = 1 });

        var preview = await Sut().ChangeClientTypeAsync(_clientId, "shop");

        Assert.Equal([("new", "new")], preview.Moves.Select(m => (m.FromKey, m.ToKey)));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _tx.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Change_needs_a_known_active_target_type()
    {
        _clients.Setup(c => c.GetAsync(_clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client { Id = _clientId, BusinessTypeCode = "shop" });
        _all[1].IsActive = false;

        Assert.Equal("The business type 'spa' isn't active.", await ErrorOf(() => Sut().ChangeClientTypeAsync(_clientId, "spa")));
        Assert.Equal("There is no business type 'nope'.", await ErrorOf(() => Sut().ChangeClientTypeAsync(_clientId, "nope")));
        await Assert.ThrowsAsync<ArgumentException>(() => Sut().ChangeClientTypeAsync(_clientId, " "));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Sut().ChangeClientTypeAsync(Guid.NewGuid(), "shop"));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
