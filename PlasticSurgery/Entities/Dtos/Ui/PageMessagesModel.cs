namespace PlasticSurgery.Entities.Dtos.Ui;

/// <summary>What Pages/Shared/_Messages shows at the top of a page (docs/UI_GUIDE.md §0.4): the page's success message
/// (a toast, or an inline alert when it is long enough to need reading) and its error message (an inline alert).</summary>
public sealed record PageMessagesModel(string? Status, string? Error);
