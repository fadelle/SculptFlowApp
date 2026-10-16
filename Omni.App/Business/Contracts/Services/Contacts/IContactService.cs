using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Contacts;
using Omni.App.Entities.Responses.Contacts;

namespace Omni.App.Business.Contracts.Services.Contacts;

public interface IContactService
{
    /// <summary>Creates a contact. If ExternalContactId is set and a contact with the same
    /// (ClientId, ExternalContactId) already exists, returns the existing contact instead of
    /// creating a duplicate — this is what makes repeated webhook deliveries safe to retry.</summary>
    Task<(ContactResponse Contact, bool WasCreated)> CreateOrGetAsync(CreateContactRequest request, CancellationToken ct = default);

    Task<ContactResponse?> GetByIdAsync(Guid clientId, Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<ContactResponse> Items, int TotalCount)> ListAsync(
        Guid clientId, string? stage, Guid? serviceId, string? search, int skip, int take, CancellationToken ct = default);

    Task<ContactResponse?> UpdateAsync(Guid clientId, Guid id, UpdateContactRequest request, CancellationToken ct = default);

    /// <summary>StageKey must be a stage key of the client's business type (ArgumentException otherwise). A real stage change
    /// stamps LastContactAt and logs an event chosen by the new stage's category (see ContactService).</summary>
    Task<ContactResponse?> UpdateStageAsync(Guid clientId, Guid id, UpdateContactStageRequest request, CancellationToken ct = default);

    /// <summary>The client's pipeline stages in order, labelled in the user's language (stage filters, badges, dropdowns).</summary>
    Task<IReadOnlyList<PipelineStageOption>> ListStagesAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Narrower update for the AI agent's update_contact tool — service interest, language,
    /// timeline, notes, follow-up date, qualification and stage (a stage key or a category) only. See
    /// UpdateContactContextRequest for why identity fields are excluded.</summary>
    Task<ContactResponse?> UpdateContextAsync(Guid clientId, Guid id, UpdateContactContextRequest request, CancellationToken ct = default);

    /// <summary>Finds the contact already on file for this phone number (regardless of how/where they
    /// were first created — a website form, another channel, etc.), or creates a new one with
    /// Source="whatsapp". Used by the WhatsApp webhook processor for an inbound customer message,
    /// where only a phone number (no contact id) is ever available from Meta. Deliberately matches by
    /// Phone first rather than only an ExternalContactId convention — a customer who already exists
    /// as a contact from a different source must not get a second, duplicate record just because they
    /// then messaged on WhatsApp.</summary>
    /// <summary>WasCreated tells the caller this is a genuinely new contact's very first inbound message — used for
    /// the NEW_CONTACT notification (see INotificationService). False for every later message from the same contact.</summary>
    Task<(ContactResponse Contact, bool WasCreated)> GetOrCreateByPhoneAsync(Guid clientId, string phone, string? fullName, CancellationToken ct = default);

    /// <summary>Finds or creates the contact for a channel-native identity that has NO phone number
    /// (Telegram today): matched strictly on (clientId, externalContactId), so the same person on two
    /// clients is two separate contacts. Never invents a phone number — Contact.Phone stays null. Safe under
    /// concurrent deliveries (the unique index on (client_id, external_contact_id) decides the winner and
    /// the loser re-reads it).</summary>
    /// <summary>WasCreated — see GetOrCreateByPhoneAsync's doc comment.</summary>
    Task<(ContactResponse Contact, bool WasCreated)> GetOrCreateByExternalIdAsync(
        Guid clientId, string externalContactId, string source, string? fullName, string? firstName, string? lastName,
        string? sourceDetail, CancellationToken ct = default);

    /// <summary>Distinct non-empty Contact.Source values on file for this client, alphabetical — backs
    /// the "Contact source" filter dropdown on the Campaign creation page rather than a hardcoded list,
    /// since Source is a free string (see Contact.cs), not a fixed enum.</summary>
    Task<IReadOnlyList<string>> GetDistinctSourcesAsync(Guid clientId, CancellationToken ct = default);
}
