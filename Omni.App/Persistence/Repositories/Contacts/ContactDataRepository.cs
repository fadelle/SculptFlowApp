using Microsoft.EntityFrameworkCore;
using Omni.App.Entities.Dtos.Contacts;
using Omni.App.Persistence.Contexts;
using Omni.App.Persistence.Contracts.Contacts;

namespace Omni.App.Persistence.Repositories.Contacts;

public class ContactDataRepository : IContactDataRepository
{
    private readonly ApplicationDbContext _db;

    public ContactDataRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ContactExportConversation>> ListConversationsAsync(Guid clientId, Guid contactId, CancellationToken ct = default) =>
        await _db.Conversations.AsNoTracking()
            .Where(c => c.ClientId == clientId && c.ContactId == contactId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new ContactExportConversation(c.Id, c.Channel, c.Status, c.CreatedAt, c.LastMessageAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ContactExportMessage>> ListMessagesAsync(Guid clientId, Guid contactId, CancellationToken ct = default) =>
        await _db.Messages.AsNoTracking()
            .Where(m => m.ClientId == clientId && m.ContactId == contactId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new ContactExportMessage(m.Id, m.ConversationId, m.Channel, m.Direction, m.SenderType, m.MessageType, m.Content,
                m.DeliveryStatus, m.CreatedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ContactExportBooking>> ListBookingsAsync(Guid clientId, Guid contactId, CancellationToken ct = default) =>
        await _db.Bookings.AsNoTracking()
            .Where(b => b.ClientId == clientId && b.ContactId == contactId)
            .OrderBy(b => b.ScheduledStart)
            .Select(b => new ContactExportBooking(b.Id, b.Service != null ? b.Service.Name : null, b.Status, b.ScheduledStart, b.ScheduledEnd,
                b.LocationName, b.Notes, b.CreatedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ContactExportOrder>> ListOrdersAsync(Guid clientId, Guid contactId, CancellationToken ct = default) =>
        await _db.Orders.AsNoTracking()
            .Where(o => o.ClientId == clientId && o.ContactId == contactId)
            .OrderBy(o => o.CreatedAt)
            .Select(o => new ContactExportOrder(o.Id, o.Service != null ? o.Service.Name : null, o.Status, o.QuotedAmount, o.DepositAmount,
                o.FinalAmount, o.CurrencyCode, o.ServiceDate, o.Notes, o.CreatedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ContactExportEvent>> ListEventsAsync(Guid clientId, Guid contactId, CancellationToken ct = default) =>
        await _db.Events.AsNoTracking()
            .Where(e => e.ClientId == clientId && e.ContactId == contactId)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new ContactExportEvent(e.EventType, e.Source, e.CreatedAt))
            .ToListAsync(ct);

    public async Task<bool> DeleteAsync(Guid clientId, Guid contactId, CancellationToken ct = default)
    {
        await _db.Notifications.Where(n => n.ClientId == clientId && n.ContactId == contactId).ExecuteDeleteAsync(ct);
        return await _db.Contacts.Where(c => c.ClientId == clientId && c.Id == contactId).ExecuteDeleteAsync(ct) > 0;
    }
}
