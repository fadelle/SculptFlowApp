using Omni.App.Entities.Dtos.Contacts;

namespace Omni.App.Persistence.Contracts.Contacts;

/// <summary>A contact's data across tables, for privacy requests: read-only projections for the export (oldest first) and
/// the permanent delete. Always scoped by client and contact.</summary>
public interface IContactDataRepository
{
    Task<IReadOnlyList<ContactExportConversation>> ListConversationsAsync(Guid clientId, Guid contactId, CancellationToken ct = default);

    Task<IReadOnlyList<ContactExportMessage>> ListMessagesAsync(Guid clientId, Guid contactId, CancellationToken ct = default);

    Task<IReadOnlyList<ContactExportBooking>> ListBookingsAsync(Guid clientId, Guid contactId, CancellationToken ct = default);

    Task<IReadOnlyList<ContactExportOrder>> ListOrdersAsync(Guid clientId, Guid contactId, CancellationToken ct = default);

    Task<IReadOnlyList<ContactExportEvent>> ListEventsAsync(Guid clientId, Guid contactId, CancellationToken ct = default);

    /// <summary>Deletes the contact (set-based). The database cascades to its conversations, messages, bookings (and their
    /// calendar sync rows), orders and campaign recipients, and clears the link on activity events; its notifications
    /// (which name the contact) are deleted first. Returns false when the contact isn't the client's.</summary>
    Task<bool> DeleteAsync(Guid clientId, Guid contactId, CancellationToken ct = default);
}
