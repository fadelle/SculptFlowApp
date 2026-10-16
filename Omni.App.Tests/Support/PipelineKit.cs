namespace Omni.App.Tests.Support;

/// <summary>
/// Pipelines for tests. <see cref="General"/> has the stages of the built-in general business type (Database/schema.sql), so
/// repository tests whose clients use that type can store its keys. <see cref="Custom"/> uses keys that match neither a
/// category nor the general type, so a test on it proves the code follows stage categories, never fixed keys.
/// </summary>
internal static class PipelineKit
{
    public const string New = "new";
    public const string Contacted = "contacted";
    public const string Qualified = "qualified";
    public const string Booked = "booked";
    public const string Attended = "attended";
    public const string NoShow = "no_show";
    public const string Customer = "customer";
    public const string NotInterested = "not_interested";
    public const string Lost = "lost";
    public const string NeedsHuman = "needs_human";

    public static ClientPipeline General() => new(
    [
        new(New, StageCategory.Open, "New"),
        new(Contacted, StageCategory.Open, "Contacted"),
        new(Qualified, StageCategory.Open, "Qualified"),
        new(Booked, StageCategory.Booked, "Booked"),
        new(Attended, StageCategory.Attended, "Attended"),
        new(NoShow, StageCategory.NoShow, "No-show"),
        new(Customer, StageCategory.Converted, "Customer"),
        new(NotInterested, StageCategory.ClosedLost, "Not interested"),
        new(Lost, StageCategory.ClosedLost, "Lost"),
        new(NeedsHuman, StageCategory.NeedsHuman, "Needs human"),
    ]);

    public static ClientPipeline Custom() => new(
    [
        new("inquiry", StageCategory.Open, "Inquiry"),
        new("in_touch", StageCategory.Open, "In touch"),
        new("visit_set", StageCategory.Booked, "Visit set"),
        new("visited", StageCategory.Attended, "Visited"),
        new("missed", StageCategory.NoShow, "Missed"),
        new("signed", StageCategory.Converted, "Signed"),
        new("gone", StageCategory.ClosedLost, "Gone"),
        new("team", StageCategory.NeedsHuman, "Team"),
        new("expert", StageCategory.NeedsHuman, "Expert question"),
    ]);

    /// <summary>Points <paramref name="types"/> at <paramref name="pipeline"/> for every client (pipeline, stage list and
    /// stage check).</summary>
    public static Mock<IBusinessTypeService> Use(this Mock<IBusinessTypeService> types, ClientPipeline pipeline)
    {
        types.Setup(t => t.GetPipelineAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(pipeline);
        types.Setup(t => t.ListStagesForClientAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(pipeline.Stages);
        types.Setup(t => t.IsStageOfClientAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string? key, CancellationToken _) => pipeline.Find(key) is not null);
        return types;
    }

    /// <summary>A business-type service that answers <paramref name="pipeline"/> (the general one by default) for every client.</summary>
    public static Mock<IBusinessTypeService> Types(ClientPipeline? pipeline = null) => new Mock<IBusinessTypeService>().Use(pipeline ?? General());
}
