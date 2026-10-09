namespace PlasticSurgery.Entities.Dtos.Ui;

/// <summary>An empty state for Pages/Shared/_EmptyState (docs/UI_GUIDE.md §6.2, §8): what is empty, and the next step.
/// Pass <see cref="Icon"/> by name (Icon: "event") so docs/tools/icon-font.js finds it.</summary>
public sealed record EmptyStateModel(
    string Icon,
    string Title,
    string? Text = null,
    string? ActionLabel = null,
    string? ActionHref = null,
    string ActionIcon = "add");
