using System.Text.Json;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.BusinessTypes;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Business.Mappers.Contacts;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Contacts;
using Omni.App.Entities.Responses.Contacts;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Contacts;

namespace Omni.App.Business.Services.Contacts;

public class ContactService : IContactService
{
    private readonly IContactRepository _contacts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEventLogger _events;
    private readonly ICatalogService _services;
    private readonly IClientRepository _clients;
    private readonly IBusinessTypeService _businessTypes;

    public ContactService(IContactRepository contacts, IUnitOfWork unitOfWork, IEventLogger events, ICatalogService services, IClientRepository clients,
        IBusinessTypeService businessTypes)
    {
        _businessTypes = businessTypes;
        _contacts = contacts;
        _unitOfWork = unitOfWork;
        _events = events;
        _services = services;
        _clients = clients;
    }

    /// <summary>Public intake (n8n / contact forms). The client must exist (404 otherwise, never a foreign-key 500) and the contact
    /// needs at least one way to identify or reach the person: a name, phone or email.</summary>
    public async Task<(ContactResponse Contact, bool WasCreated)> CreateOrGetAsync(CreateContactRequest request, CancellationToken ct = default)
    {
        if (await _clients.GetReadOnlyAsync(request.ClientId, ct) is null)
        {
            throw new KeyNotFoundException("clients.errors.notFound");
        }

        if (!string.IsNullOrWhiteSpace(request.ExternalContactId))
        {
            var existing = await _contacts.FindByExternalIdAsync(request.ClientId, request.ExternalContactId, ct);

            if (existing is not null)
            {
                return (ContactMapper.ToResponse(existing), false);
            }
        }

        if (new[] { request.FullName, request.FirstName, request.LastName, request.Phone, request.Email }.All(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("contacts.errors.identityRequired");
        }

        var pipeline = await _businessTypes.GetPipelineAsync(request.ClientId, ct);

        var contact = new Contact
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            ServiceId = request.ServiceId,
            FullName = request.FullName,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Phone = request.Phone,
            Email = request.Email,
            Source = request.Source,
            SourceDetail = request.SourceDetail,
            CampaignName = request.CampaignName,
            ExternalContactId = request.ExternalContactId,
            PreferredLanguage = request.PreferredLanguage,
            City = request.City,
            DesiredTimeline = request.DesiredTimeline,
            Notes = request.Notes,
            MarketingOptIn = request.MarketingOptIn,
            StageKey = pipeline.EntryKey,
            Qualification = ContactQualification.Unknown,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _contacts.Add(contact);
        _events.Log(contact.ClientId, EventTypes.ContactCreated, contactId: contact.Id, source: contact.Source);

        await _unitOfWork.SaveChangesAsync(ct);

        if (contact.ServiceId is not null)
        {
            await _contacts.LoadServiceAsync(contact, ct);
        }

        return (ContactMapper.ToResponse(contact), true);
    }

    public async Task<ContactResponse?> GetByIdAsync(Guid clientId, Guid id, CancellationToken ct = default)
    {
        var contact = await _contacts.GetAsync(clientId, id, ct);

        return contact is null ? null : ContactMapper.ToResponse(contact);
    }

    public async Task<(IReadOnlyList<ContactResponse> Items, int TotalCount)> ListAsync(
        Guid clientId, string? stage, Guid? serviceId, string? search, int skip, int take, CancellationToken ct = default)
    {
        var (items, totalCount) = await _contacts.ListAsync(clientId, stage, serviceId, search, skip, take, ct);
        return (items.Select(ContactMapper.ToResponse).ToList(), totalCount);
    }

    public async Task<ContactResponse?> UpdateAsync(Guid clientId, Guid id, UpdateContactRequest request, CancellationToken ct = default)
    {
        var contact = await _contacts.GetAsync(clientId, id, ct);

        if (contact is null) return null;

        // Only a CHANGE of service interest is validated — resubmitting the contact's current
        // (possibly now-inactive) service must keep working.
        if (request.ServiceId is { } newServiceId && newServiceId != contact.ServiceId)
        {
            await _services.EnsureUsableAsync(clientId, newServiceId, ct);
        }

        if (request.ServiceId is not null) contact.ServiceId = request.ServiceId;
        if (request.FullName is not null) contact.FullName = request.FullName;
        if (request.FirstName is not null) contact.FirstName = request.FirstName;
        if (request.LastName is not null) contact.LastName = request.LastName;
        if (request.Phone is not null) contact.Phone = request.Phone;
        if (request.Email is not null) contact.Email = request.Email;
        if (request.PreferredLanguage is not null) contact.PreferredLanguage = request.PreferredLanguage;
        if (request.City is not null) contact.City = request.City;
        if (request.DesiredTimeline is not null) contact.DesiredTimeline = request.DesiredTimeline;
        if (request.Notes is not null) contact.Notes = request.Notes;
        if (request.NextFollowupAt is not null) contact.NextFollowupAt = request.NextFollowupAt;

        contact.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.ServiceId is not null)
        {
            await _contacts.LoadServiceAsync(contact, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return ContactMapper.ToResponse(contact);
    }

    public async Task<ContactResponse?> UpdateStageAsync(Guid clientId, Guid id, UpdateContactStageRequest request, CancellationToken ct = default)
    {
        var pipeline = await _businessTypes.GetPipelineAsync(clientId, ct);
        if (pipeline.Find(request.StageKey) is null)
        {
            throw new ArgumentException("contacts.errors.unknownStage").WithArgs(request.StageKey);
        }

        if (request.Qualification is not null && !ContactQualification.All.Contains(request.Qualification))
        {
            throw new ArgumentException("contacts.errors.invalidQualification").WithArgs(request.Qualification);
        }

        var contact = await _contacts.GetAsync(clientId, id, ct);

        if (contact is null) return null;

        ChangeStage(contact, pipeline, request.StageKey);
        if (request.Qualification is not null)
        {
            contact.Qualification = request.Qualification;
        }
        if (!string.IsNullOrWhiteSpace(request.Source))
        {
            contact.Source = request.Source;
        }

        contact.UpdatedAt = DateTimeOffset.UtcNow;

        await _unitOfWork.SaveChangesAsync(ct);
        return ContactMapper.ToResponse(contact);
    }

    /// <summary>Moves the contact to <paramref name="stageKey"/> (already checked against <paramref name="pipeline"/>). Only a
    /// real stage change counts as "contact": a member correction to qualification/source alone (stage resubmitted unchanged)
    /// must not reset LastContactAt, since the reactivation audience (CampaignAudienceService) uses it as the inactivity
    /// clock. The event follows the new stage's category, so it reads the same for every business type.</summary>
    private void ChangeStage(Contact contact, ClientPipeline pipeline, string stageKey)
    {
        var from = contact.StageKey;
        if (from == stageKey) return;

        contact.StageKey = stageKey;
        contact.LastContactAt = DateTimeOffset.UtcNow;
        var eventType = pipeline.CategoryOf(stageKey) switch
        {
            StageCategory.Booked => EventTypes.BookingCreated,
            StageCategory.Attended => EventTypes.BookingAttended,
            StageCategory.NoShow => EventTypes.BookingNoShow,
            StageCategory.NeedsHuman => EventTypes.HumanHandoff,
            _ => EventTypes.ContactStageChanged
        };
        _events.Log(contact.ClientId, eventType, contactId: contact.Id,
            metadataJson: JsonSerializer.Serialize(new { from, to = stageKey }));
    }

    public Task<IReadOnlyList<PipelineStageOption>> ListStagesAsync(Guid clientId, CancellationToken ct = default) =>
        _businessTypes.ListStagesForClientAsync(clientId, ct);

    public async Task<ContactResponse?> UpdateContextAsync(Guid clientId, Guid id, UpdateContactContextRequest request, CancellationToken ct = default)
    {
        if (request.Qualification is not null && !ContactQualification.All.Contains(request.Qualification))
        {
            throw new ArgumentException("contacts.errors.invalidQualification").WithArgs(request.Qualification);
        }

        var pipeline = ClientPipeline.Empty;
        string? stageKey = null;
        if (!string.IsNullOrWhiteSpace(request.Stage))
        {
            pipeline = await _businessTypes.GetPipelineAsync(clientId, ct);
            stageKey = pipeline.Resolve(request.Stage)
                ?? throw new ArgumentException("contacts.errors.unknownStage").WithArgs(request.Stage.Trim());
        }

        var contact = await _contacts.GetAsync(clientId, id, ct);
        if (contact is null) return null;

        if (request.ServiceId is { } newServiceId && newServiceId != contact.ServiceId)
        {
            await _services.EnsureUsableAsync(clientId, newServiceId, ct);
        }

        if (stageKey is not null) ChangeStage(contact, pipeline, stageKey);
        if (request.ServiceId is not null) contact.ServiceId = request.ServiceId;
        if (request.PreferredLanguage is not null) contact.PreferredLanguage = request.PreferredLanguage;
        if (request.City is not null) contact.City = request.City;
        if (request.DesiredTimeline is not null) contact.DesiredTimeline = request.DesiredTimeline;
        if (request.Notes is not null) contact.Notes = request.Notes;
        if (request.NextFollowupAt is not null) contact.NextFollowupAt = request.NextFollowupAt;
        if (request.Qualification is not null) contact.Qualification = request.Qualification;

        contact.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.ServiceId is not null)
        {
            await _contacts.LoadServiceAsync(contact, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return ContactMapper.ToResponse(contact);
    }

    public async Task<(ContactResponse Contact, bool WasCreated)> GetOrCreateByPhoneAsync(Guid clientId, string phone, string? fullName, CancellationToken ct = default)
    {
        var existing = await _contacts.FindByPhoneAsync(clientId, phone, ct);
        if (existing is not null) return (ContactMapper.ToResponse(existing), false);

        var pipeline = await _businessTypes.GetPipelineAsync(clientId, ct);

        var now = DateTimeOffset.UtcNow;
        var contact = new Contact
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FullName = fullName,
            Phone = phone,
            Source = "whatsapp",
            ExternalContactId = $"whatsapp:{phone}",
            StageKey = pipeline.EntryKey,
            Qualification = ContactQualification.Unknown,
            MarketingOptIn = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        _contacts.Add(contact);
        _events.Log(clientId, EventTypes.ContactCreated, contactId: contact.Id, source: "whatsapp");
        await _unitOfWork.SaveChangesAsync(ct);

        return (ContactMapper.ToResponse(contact), true);
    }

    public async Task<(ContactResponse Contact, bool WasCreated)> GetOrCreateByExternalIdAsync(
        Guid clientId, string externalContactId, string source, string? fullName, string? firstName, string? lastName,
        string? sourceDetail, CancellationToken ct = default)
    {
        var existing = await _contacts.FindByExternalIdAsync(clientId, externalContactId, ct);
        if (existing is not null) return (ContactMapper.ToResponse(existing), false);

        var pipeline = await _businessTypes.GetPipelineAsync(clientId, ct);

        var now = DateTimeOffset.UtcNow;
        var contact = new Contact
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            FullName = fullName,
            FirstName = firstName,
            LastName = lastName,
            Phone = null,
            Source = source,
            SourceDetail = sourceDetail,
            ExternalContactId = externalContactId,
            StageKey = pipeline.EntryKey,
            Qualification = ContactQualification.Unknown,
            MarketingOptIn = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        _contacts.Add(contact);
        _events.Log(clientId, EventTypes.ContactCreated, contactId: contact.Id, source: source);

        if (!await _unitOfWork.TrySaveChangesAsync(ct))
        {
            // A concurrent delivery created the same contact first — our pending contact/event were dropped; use theirs.
            var winner = await _contacts.FindByExternalIdAsync(clientId, externalContactId, ct)
                ?? throw new InvalidOperationException("Contact conflict reported but the existing contact was not found."); // text-ok: internal invariant
            return (ContactMapper.ToResponse(winner), false); // a concurrent delivery created it first — not "our" new contact
        }

        return (ContactMapper.ToResponse(contact), true);
    }

    public Task<IReadOnlyList<string>> GetDistinctSourcesAsync(Guid clientId, CancellationToken ct = default) =>
        _contacts.ListDistinctSourcesAsync(clientId, ct);
}
