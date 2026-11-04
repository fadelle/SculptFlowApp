namespace Omni.App.Entities.Requests.Inbox;

/// <summary>One page of the Inbox list: a search over the contact's name, phone and service, a tab (InboxListFilter, empty =
/// all) and the page's position, newest activity first.</summary>
public record ConversationListRequest(string? Search, string? Filter, int Skip, int Take);
