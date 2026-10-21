using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Contacts;

namespace Omni.App.Business.Contracts.Services.Contacts;

/// <summary>
/// A contact's opt-out of business-started messages (crm.contacts.marketing_opt_in / opted_out_at): campaigns, booking
/// reminders and Inbox templates skip or refuse an opted-out contact; replies in a conversation the contact opens again are
/// still allowed. A contact opts out or back in with a whole-message stop/start word (OptOut:* settings, see
/// OptOutKeywords; MessageService applies it to inbound messages) or a member changes it on the contact page.
/// </summary>
public interface IOptOutService
{
    /// <summary>true = the text is a stop word, false = a start word, null = neither or OptOut:Enabled is off.</summary>
    bool? MatchKeyword(string? text);

    /// <summary>Sets the contact's opt-out state on the tracked entity (no save) and logs the event when it changed.
    /// Returns whether it changed.</summary>
    bool Apply(Contact contact, bool optedOut, string source, Guid? conversationId = null);

    /// <summary>The one confirmation sent back after a stop/start word, in the contact's language (stored language, else
    /// the client's default) and naming the business; null when OptOut:SendConfirmation is off.</summary>
    Task<string?> ConfirmationTextAsync(Guid clientId, Contact contact, bool optedOut, CancellationToken ct = default);

    /// <summary>A member opts a contact of their client out or back in (contact page). Null when the contact isn't the
    /// client's.</summary>
    Task<ContactResponse?> SetAsync(Guid clientId, Guid contactId, bool optedOut, CancellationToken ct = default);
}
