using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Business.Mappers.Contacts;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Contacts;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Contacts;

namespace Omni.App.Business.Services.Contacts;

public class OptOutService : IOptOutService
{
    private readonly IConfigManager _config;
    private readonly IContactRepository _contacts;
    private readonly IClientRepository _clients;
    private readonly IReaderTextFactory _readerText;
    private readonly IEventLogger _events;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public OptOutService(IConfigManager config, IContactRepository contacts, IClientRepository clients, IReaderTextFactory readerText,
        IEventLogger events, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _config = config;
        _contacts = contacts;
        _clients = clients;
        _readerText = readerText;
        _events = events;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public bool? MatchKeyword(string? text) =>
        _config.OptOutEnabled ? OptOutKeywords.Match(text, _config.OptOutStopKeywords, _config.OptOutStartKeywords) : null;

    public bool Apply(Contact contact, bool optedOut, string source, Guid? conversationId = null)
    {
        var isOptedOut = contact.OptedOutAt is not null || !contact.MarketingOptIn;
        if (isOptedOut == optedOut) return false;
        var now = _time.GetUtcNow();
        contact.MarketingOptIn = !optedOut;
        contact.OptedOutAt = optedOut ? now : null;
        contact.UpdatedAt = now;
        _events.Log(contact.ClientId, optedOut ? EventTypes.ContactOptedOut : EventTypes.ContactOptedIn,
            contactId: contact.Id, conversationId: conversationId, source: source);
        return true;
    }

    public async Task<string?> ConfirmationTextAsync(Guid clientId, Contact contact, bool optedOut, CancellationToken ct = default)
    {
        if (!_config.OptOutSendConfirmation) return null;
        var client = await _clients.GetReadOnlyAsync(clientId, ct);
        var text = await _readerText.ForAsync([contact.PreferredLanguage, client?.DefaultLanguage], client?.BusinessTypeCode, ct);
        var name = client?.Name ?? string.Empty;
        return optedOut ? text["optOut.confirmation.stopped", name] : text["optOut.confirmation.resumed", name];
    }

    public async Task<ContactResponse?> SetAsync(Guid clientId, Guid contactId, bool optedOut, CancellationToken ct = default)
    {
        var contact = await _contacts.GetAsync(clientId, contactId, ct);
        if (contact is null) return null;
        if (Apply(contact, optedOut, MessageOrigin.Dashboard)) await _unitOfWork.SaveChangesAsync(ct);
        return ContactMapper.ToResponse(contact);
    }
}
