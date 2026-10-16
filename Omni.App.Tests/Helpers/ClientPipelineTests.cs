namespace Omni.App.Tests.Helpers;

public class ClientPipelineTests
{
    private static readonly ClientPipeline Custom = PipelineKit.Custom();

    [Fact]
    public void New_contacts_enter_at_the_first_open_stage_or_the_first_stage()
    {
        Assert.Equal("inquiry", Custom.EntryKey);
        Assert.Equal("later", new ClientPipeline([new("later", StageCategory.ClosedLost, "Later"), new("x", StageCategory.Booked, "X")]).EntryKey);
        Assert.Throws<InvalidOperationException>(() => ClientPipeline.Empty.EntryKey);
    }

    [Fact]
    public void Lookups_by_key_and_category()
    {
        Assert.Equal(StageCategory.Booked, Custom.CategoryOf("visit_set"));
        Assert.Null(Custom.CategoryOf("nope"));
        Assert.Null(Custom.Find(null));
        Assert.Equal("team", Custom.FirstOf(StageCategory.NeedsHuman)!.Key);
        Assert.Equal(["gone", "team", "expert"], Custom.KeysOf(StageCategory.NeedsHuman, StageCategory.ClosedLost)); // pipeline order
    }

    [Theory]
    [InlineData("inquiry", StageCategory.Booked, "visit_set")]
    [InlineData("expert", StageCategory.NeedsHuman, "expert")]  // already in that category: stays
    [InlineData("gone", StageCategory.NeedsHuman, "team")]
    [InlineData("unknown_key", StageCategory.Converted, "signed")]
    public void MoveTo_keeps_a_stage_already_in_the_category(string from, string category, string expected) =>
        Assert.Equal(expected, Custom.MoveTo(from, category));

    [Fact]
    public void MoveTo_a_category_the_type_lacks_is_null() =>
        Assert.Null(new ClientPipeline([new("a", StageCategory.Open, "A")]).MoveTo("a", StageCategory.Booked));

    [Theory]
    [InlineData("visited", "visited")]          // a key
    [InlineData("attended", "visited")]         // a category
    [InlineData(" no_show ", "missed")]
    [InlineData("customer", null)]              // another type's key
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Resolve_takes_a_key_or_a_category(string? value, string? expected) => Assert.Equal(expected, Custom.Resolve(value));

    [Fact]
    public void A_key_wins_over_a_category_of_the_same_name()
    {
        var pipeline = new ClientPipeline([new("new", StageCategory.Open, "New"), new("open", StageCategory.Open, "Open"), new("booked", StageCategory.Booked, "B")]);
        Assert.Equal("open", pipeline.Resolve("open"));
    }

    [Fact]
    public void The_next_open_stage_follows_only_the_entry_stage()
    {
        Assert.Equal("in_touch", Custom.NextAfterEntry("inquiry"));
        Assert.Null(Custom.NextAfterEntry("in_touch"));
        Assert.Null(new ClientPipeline([new("only", StageCategory.Open, "Only"), new("b", StageCategory.Booked, "B")]).NextAfterEntry("only"));
        Assert.Null(ClientPipeline.Empty.NextAfterEntry("x"));
    }

    [Fact]
    public void Stage_display_uses_the_label_and_category_colour_and_falls_back_for_unknown_keys()
    {
        Assert.Equal(("Visit set", "badge-amber"), (StageDisplay.Label(Custom.Stages, "visit_set"), StageDisplay.CssClass(Custom.Stages, "visit_set")));
        Assert.Equal(("Old stage", "badge-gray"), (StageDisplay.Label(Custom.Stages, "old_stage"), StageDisplay.CssClass(Custom.Stages, "old_stage")));
    }
}
