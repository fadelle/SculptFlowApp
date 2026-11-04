namespace Omni.App.Entities.Dtos.PlatformAdmin;

/// <summary>Conversations left without an answer: their last message is the customer's, from <see cref="From"/> to
/// <see cref="Before"/> (now minus the minutes it may wait), oldest wait first.</summary>
public record UnansweredPage(DateTimeOffset From, DateTimeOffset Before, int MinutesWithoutAnswer, int Page, int PageSize, int Total,
    IReadOnlyList<UnansweredRow> Items);
