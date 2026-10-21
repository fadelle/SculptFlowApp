using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Business.Mappers.Contacts;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Clients;
using Omni.App.Entities.Dtos.Contacts;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Contacts;

namespace Omni.App.Business.Services.Contacts;

public class ContactDataService : IContactDataService
{
    private const string ConfirmMismatchKey = "contactDetail.data.errors.confirmMismatch";

    private readonly IContactRepository _contacts;
    private readonly IContactDataRepository _data;
    private readonly IClientRepository _clients;
    private readonly IEventLogger _events;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;
    private readonly ILogger<ContactDataService> _logger;

    public ContactDataService(IContactRepository contacts, IContactDataRepository data, IClientRepository clients, IEventLogger events,
        IUnitOfWork unitOfWork, TimeProvider time, ILogger<ContactDataService> logger)
    {
        _contacts = contacts;
        _data = data;
        _clients = clients;
        _events = events;
        _unitOfWork = unitOfWork;
        _time = time;
        _logger = logger;
    }

    public async Task<ContactExport?> ExportAsync(UserAccess actor, Guid contactId, CancellationToken ct = default)
    {
        ContactDataRules.EnsureClientRole(actor);
        var contact = await _contacts.GetAsync(actor.ClientId, contactId, ct);
        return contact is null ? null : await BuildAsync(contact, ct);
    }

    public async Task<ContactExport?> ExportAnyAsync(Guid contactId, CancellationToken ct = default)
    {
        var contact = await _contacts.GetByIdAsync(contactId, ct);
        return contact is null ? null : await BuildAsync(contact, ct);
    }

    public async Task<bool> DeleteAsync(UserAccess actor, Guid contactId, string? confirmation, CancellationToken ct = default)
    {
        ContactDataRules.EnsureClientRole(actor);
        var contact = await _contacts.GetAsync(actor.ClientId, contactId, ct);
        if (contact is null) return false;
        if (!ContactDataRules.Confirms(confirmation, ContactDataRules.ConfirmationText(contact.FullName, contact.Phone, contact.Email)))
            throw new ArgumentException(ConfirmMismatchKey);
        await DeleteCoreAsync(contact, ct);
        return true;
    }

    public async Task<Guid?> DeleteAnyAsync(Guid contactId, CancellationToken ct = default)
    {
        var contact = await _contacts.GetByIdAsync(contactId, ct);
        if (contact is null) return null;
        await DeleteCoreAsync(contact, ct);
        return contact.ClientId;
    }

    private async Task DeleteCoreAsync(Contact contact, CancellationToken ct)
    {
        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        await _data.DeleteAsync(contact.ClientId, contact.Id, ct);
        // The tracked copy is gone from the database; don't let a later save try to update it.
        _unitOfWork.DiscardChanges();
        _events.Log(contact.ClientId, EventTypes.ContactDeleted, source: "privacy_request");
        await _unitOfWork.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _logger.LogInformation("Contact {ContactId} of client {ClientId} deleted with its data.", contact.Id, contact.ClientId);
    }

    private async Task<ContactExport> BuildAsync(Contact contact, CancellationToken ct)
    {
        var client = await _clients.GetReadOnlyAsync(contact.ClientId, ct);
        return new ContactExport(
            _time.GetUtcNow(),
            client?.Name ?? string.Empty,
            ContactMapper.ToResponse(contact),
            await _data.ListConversationsAsync(contact.ClientId, contact.Id, ct),
            await _data.ListMessagesAsync(contact.ClientId, contact.Id, ct),
            await _data.ListBookingsAsync(contact.ClientId, contact.Id, ct),
            await _data.ListOrdersAsync(contact.ClientId, contact.Id, ct),
            await _data.ListEventsAsync(contact.ClientId, contact.Id, ct));
    }
}
