using Omni.App.Entities.Responses.Contacts;

namespace Omni.App.Entities.Dtos.Contacts;

/// <summary>Everything Omni holds about one contact, for a privacy request (GET /api/contacts/{id}/export): the contact,
/// its conversations and every message, bookings, orders and activity, oldest first. Written as one JSON file.</summary>
public record ContactExport(
    DateTimeOffset ExportedAt,
    string ClientName,
    ContactResponse Contact,
    IReadOnlyList<ContactExportConversation> Conversations,
    IReadOnlyList<ContactExportMessage> Messages,
    IReadOnlyList<ContactExportBooking> Bookings,
    IReadOnlyList<ContactExportOrder> Orders,
    IReadOnlyList<ContactExportEvent> Activity);
