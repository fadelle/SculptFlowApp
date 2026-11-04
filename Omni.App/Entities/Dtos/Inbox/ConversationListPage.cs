namespace Omni.App.Entities.Dtos.Inbox;

/// <summary>One page of the Inbox list: its rows, how many match the search and tab (TotalCount), each tab's count, and the
/// customer messages waiting unread in every conversation (the sidebar badge and the browser-tab title).</summary>
public record ConversationListPage(IReadOnlyList<ConversationListRow> Items, int TotalCount, InboxListCounts Counts, int UnreadTotal);
