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
using Omni.App.Entities.Dtos.Monitoring;
using Omni.App.Entities.Dtos.WhatsApp;
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
    private readonly IMessageTracer _tracer;

    public ContactService(IContactRepository contacts, IUnitOfWork unitOfWork, IEventLogger events, ICatalogService services, IClientRepository clients,
        IBusinessTypeService businessTypes, IMessageTracer tracer)
    {
        _tracer = tracer;
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

        var language = LanguageOrThrow(request.PreferredLanguage);
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
            PreferredLanguage = language,
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

        // Everything is checked before the contact changes, so a refusal leaves it as it was.
        var name = request.FullName is null ? contact.FullName : NameOrThrow(request.FullName);
        var firstName = request.FirstName is null ? contact.FirstName : Limited(request.FirstName, PartNameMax, () => new ArgumentException("contacts.errors.nameTooLong"));
        var lastName = request.LastName is null ? contact.LastName : Limited(request.LastName, PartNameMax, () => new ArgumentException("contacts.errors.nameTooLong"));
        var phone = request.Phone is null ? contact.Phone : PhoneOrThrow(request.Phone);
        var email = request.Email is null ? contact.Email : EmailOrThrow(request.Email);
        var city = request.City is null ? contact.City : Limited(request.City, CityMax, () => new ArgumentException("contacts.errors.cityTooLong"));
        var timeline = request.DesiredTimeline is null ? contact.DesiredTimeline : Limited(request.DesiredTimeline, TimelineMax, () => new ArgumentException("contacts.errors.timelineTooLong"));
        var language = request.PreferredLanguage is null ? contact.PreferredLanguage : LanguageOrThrow(request.PreferredLanguage);
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(phone))
        {
            throw new ArgumentException("contacts.errors.nameOrPhoneRequired");
        }
        if (phone is not null && phone != contact.Phone
            && await _contacts.FindByPhoneAsync(clientId, phone, ct) is { } other && other.Id != contact.Id)
        {
            throw new ArgumentException("contacts.errors.phoneTaken").WithArgs(other.FullName ?? phone);
        }

        if (request.ServiceId is not null) contact.ServiceId = request.ServiceId;
        contact.FullName = name;
        contact.FirstName = firstName;
        contact.LastName = lastName;
        contact.Phone = phone;
        contact.Email = email;
        contact.PreferredLanguage = language;
        contact.City = city;
        contact.DesiredTimeline = timeline;
        if (request.Notes is not null) contact.Notes = Clean(request.Notes);
        if (request.NextFollowupAt is not null) contact.NextFollowupAt = request.NextFollowupAt.Value.ToUniversalTime();
        else if (request.ClearNextFollowup) contact.NextFollowupAt = null;

        contact.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.ServiceId is not null)
        {
            await _contacts.LoadServiceAsync(contact, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return ContactMapper.ToResponse(contact);
    }

    public async Task<ContactResponse> AddManualAsync(Guid clientId, ManualContactRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = NameOrThrow(request.FullName);
        var phone = PhoneOrThrow(request.Phone);
        var email = EmailOrThrow(request.Email);
        if (name is null && phone is null)
        {
            throw new ArgumentException("contacts.errors.nameOrPhoneRequired");
        }
        if (phone is not null && await _contacts.FindByPhoneAsync(clientId, phone, ct) is { } existing)
        {
            throw new ArgumentException("contacts.errors.phoneTaken").WithArgs(existing.FullName ?? phone);
        }
        if (request.ServiceId is { } serviceId)
        {
            await _services.EnsureUsableAsync(clientId, serviceId, ct);
        }

        var (contact, _) = await CreateOrGetAsync(new CreateContactRequest(clientId, request.ServiceId, name, null, null, phone, email,
            ContactSource.Manual, null, null, null, null, null, null, Clean(request.Notes)), ct);
        return contact;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Column sizes of crm.contacts: a longer value is a 400 with the limit, never a database error (QA t3-inbox-08).
    private const int NameMax = 200, PartNameMax = 100, EmailMax = 200, CityMax = 100, TimelineMax = 100;

    /// <summary>Trimmed text (blank = cleared) no longer than <paramref name="max"/> characters; longer throws <paramref name="tooLong"/>
    /// with the limit (the keys stay literal for the text guard).</summary>
    private static string? Limited(string? value, int max, Func<ArgumentException> tooLong)
    {
        var clean = Clean(value);
        return clean is null || clean.Length <= max ? clean : throw tooLong().WithArgs(max);
    }

    private static string? NameOrThrow(string? name) => Limited(name, NameMax, () => new ArgumentException("contacts.errors.nameTooLong"));

    /// <summary>A phone typed in the dashboard, stored the way channels store it: digits only (WhatsApp's wa_id has no "+"); blank
    /// clears it. Letters, or fewer than 6 or more than 15 digits, aren't a phone number (400).</summary>
    private static string? PhoneOrThrow(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsAsciiDigit).ToArray());
        var onlyPhoneCharacters = phone.All(c => char.IsAsciiDigit(c) || c is ' ' or '+' or '-' or '(' or ')' or '.');
        return onlyPhoneCharacters && digits.Length is >= 6 and <= 15
            ? digits
            : throw new ArgumentException("contacts.errors.invalidPhone").WithArgs(phone.Trim().Length > 40 ? phone.Trim()[..40] + "…" : phone.Trim());
    }

    private static string? EmailOrThrow(string? email)
    {
        var clean = Limited(email, EmailMax, () => new ArgumentException("contacts.errors.emailTooLong"));
        return clean is null || EmailAddress.IsValid(clean) ? clean : throw new ArgumentException("contacts.errors.invalidEmail").WithArgs(clean);
    }

    /// <summary>A language a person typed or picked in the dashboard: blank clears it, a language name or code becomes its
    /// two-letter code (ContactLanguage), anything else is refused (400) so nobody saves text the AI can't use.</summary>
    private static string? LanguageOrThrow(string? language) =>
        string.IsNullOrWhiteSpace(language) ? null
            : ContactLanguage.Normalize(language) ?? throw new ArgumentException("contacts.errors.invalidLanguage").WithArgs(language.Trim());

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

        var language = request.PreferredLanguage is null ? null : LanguageOrThrow(request.PreferredLanguage);

        var contact = await _contacts.GetAsync(clientId, id, ct);

        if (contact is null) return null;

        ChangeStage(contact, pipeline, request.StageKey);
        if (request.PreferredLanguage is not null) contact.PreferredLanguage = language;
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
        if (!string.IsNullOrWhiteSpace(request.PreferredLanguage)) SetLanguageFromAi(contact, request.PreferredLanguage);
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

    /// <summary>The AI's update_contact: a language it names becomes the contact's two-letter code. Text it can't map leaves
    /// the stored language as it was (an unclear guess must not erase a known language) and records a
    /// contact.language_ignored step in the message trace, so the bot's mistake is visible there.</summary>
    private void SetLanguageFromAi(Contact contact, string language)
    {
        if (ContactLanguage.Normalize(language) is { } code)
        {
            contact.PreferredLanguage = code;
            return;
        }

        var value = language.Trim();
        _tracer.Step(TraceSteps.ContactLanguageIgnored, TraceOutcomes.Skipped, new TraceIds(contact.ClientId),
            detail: new { contactId = contact.Id, value = value[..Math.Min(value.Length, 20)], kept = contact.PreferredLanguage });
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
        if (!await _unitOfWork.TrySaveChangesAsync(ct))
        {
            // Two first messages from one new number at once (QA t10-10: the loser was a 500): the other delivery created it.
            var winner = await _contacts.FindByPhoneAsync(clientId, phone, ct)
                ?? throw new InvalidOperationException("Contact conflict reported but the existing contact was not found."); // text-ok: internal invariant
            return (ContactMapper.ToResponse(winner), false);
        }

        return (ContactMapper.ToResponse(contact), true);
    }

    /// <summary>The source a contact gets when their first message came from a click-to-WhatsApp ad.</summary>
    public const string AdSource = "whatsapp_ad";

    public async Task ApplyAdReferralAsync(Guid clientId, Guid contactId, MetaAdReferral referral, CancellationToken ct = default)
    {
        var contact = await _contacts.GetAsync(clientId, contactId, ct);
        if (contact is null) return;
        var untouched = contact.Source is null or "whatsapp" && string.IsNullOrWhiteSpace(contact.SourceDetail);
        if (!untouched) return;
        contact.Source = AdSource;
        contact.SourceDetail = Clip(referral.Summary, 200);
        contact.CampaignName = referral.SourceId is null ? contact.CampaignName : Clip($"Ad {referral.SourceId}", 200);
        contact.UpdatedAt = DateTimeOffset.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<bool> ChangePhoneAsync(Guid clientId, Guid contactId, string newPhone, CancellationToken ct = default)
    {
        var contact = await _contacts.GetAsync(clientId, contactId, ct);
        if (contact is null || string.Equals(contact.Phone, newPhone, StringComparison.Ordinal)) return false;
        if (await _contacts.FindByPhoneAsync(clientId, newPhone, ct) is { } other && other.Id != contactId)
        {
            _events.Log(clientId, EventTypes.ContactPhoneChanged, contactId: contact.Id, source: "whatsapp",
                metadataJson: JsonSerializer.Serialize(new { from = contact.Phone, to = newPhone, applied = false, otherContactId = other.Id }));
            await _unitOfWork.SaveChangesAsync(ct);
            return false;
        }
        var previous = contact.Phone;
        contact.Phone = newPhone;
        // The WhatsApp identity follows the number (it is how a message finds its contact).
        if (contact.ExternalContactId == $"whatsapp:{previous}") contact.ExternalContactId = $"whatsapp:{newPhone}";
        contact.UpdatedAt = DateTimeOffset.UtcNow;
        _events.Log(clientId, EventTypes.ContactPhoneChanged, contactId: contact.Id, source: "whatsapp",
            metadataJson: JsonSerializer.Serialize(new { from = previous, to = newPhone, applied = true }));
        await _unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    private static string? Clip(string? value, int max) => value is null || value.Length <= max ? value : value[..max];

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
