namespace Omni.App.Entities.Dtos.Inbox;

/// <summary>How many conversations each Inbox tab holds for the current search (over every conversation, not one page).</summary>
public record InboxListCounts(int All, int Needs, int Ai, int Closed);
