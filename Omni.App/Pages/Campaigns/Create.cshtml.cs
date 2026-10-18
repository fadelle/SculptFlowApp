using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Campaigns;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Business.Contracts.Services.Catalog;
using Omni.App.Business.Contracts.Services.WhatsApp;
using Omni.App.Business.Services.Inbox;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Campaigns;
using Omni.App.Entities.Requests.Campaigns;
using Omni.App.Entities.Responses.Contacts;
using Omni.App.Entities.Responses.Catalog;
using Omni.App.Entities.Responses.WhatsApp;

namespace Omni.App.Pages.Campaigns;

/// <summary>
/// Campaign creation: pick an approved template, pick an audience, map body variables, and choose
/// send-now vs schedule-later.
///
/// Audience is presented as three business-framed choices — "Old contacts who never booked"
/// (AudienceType.ReactivationNoBooking, the flagship reactivation feature), "All contactable
/// contacts" (AllEligible), and "Build a custom audience" (Custom, with a progressive-disclosure
/// filter builder: a couple of common fields up front, the rest behind "Advanced filters" — see
/// Create.cshtml). Internal enum values (all_eligible, reactivation_no_booking, custom) never
/// reach the UI text, only the radio `value`s wwwroot/js/campaign-audience.js reads.
///
/// "Build a custom audience" also has a "pick contacts yourself" escape hatch (ManualSelection) that
/// reveals the original search + checkbox list — still audience_type = custom under the hood, just
/// with an explicit ContactIds list instead of AudienceFilters (see CampaignService.CreateAsync: an
/// explicit list always wins over filters when both could apply).
///
/// See wwwroot/js/campaign-audience.js for the live match-count preview and ICampaignAudienceService
/// for how each audience actually resolves to a contact list. Each variable box may contain literal
/// text or a {ContactX} token, resolved per-contact before calling ICampaignService.CreateAsync — but
/// that per-contact resolution needs to know the contact list up front, so it's only available for the
/// manual selection (see OnPostAsync).
/// </summary>
public class CreateModel : PageModel
{
    private static readonly Regex PlaceholderPattern = new(@"\{\{(\d+)\}\}", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions AudienceFiltersJsonOptions = new(JsonSerializerDefaults.Web);

    private const string NameAndTemplateKey = "campaigns.create.errors.nameAndTemplate";
    private const string PickSomeoneKey = "campaigns.create.errors.pickSomeone";
    private const string BlanksNeedManualKey = "campaigns.create.errors.blanksNeedManual";
    private const string ScheduleMissingKey = "campaigns.create.errors.scheduleMissing";
    private const string SchedulePastKey = "campaigns.create.errors.schedulePast";

    private readonly ICurrentClientContext _clientContext;
    private readonly IWhatsAppTemplateService _templates;
    private readonly IContactService _contacts;
    private readonly ICatalogService _services;
    private readonly ICampaignService _campaigns;
    private readonly IStringLocalizer _text;

    public CreateModel(ICurrentClientContext clientContext, IWhatsAppTemplateService templates, IContactService contacts, ICatalogService services,
        ICampaignService campaigns, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _templates = templates;
        _contacts = contacts;
        _services = services;
        _campaigns = campaigns;
        _text = text;
    }

    public bool ClientConfigured { get; private set; }
    public IReadOnlyList<WhatsAppTemplateResponse> ApprovedTemplates { get; private set; } = Array.Empty<WhatsAppTemplateResponse>();
    public IReadOnlyList<ContactResponse> Contacts { get; private set; } = Array.Empty<ContactResponse>();
    public IReadOnlyList<ServiceResponse> Services { get; private set; } = Array.Empty<ServiceResponse>();
    public IReadOnlyList<string> ContactSources { get; private set; } = Array.Empty<string>();
    public WhatsAppTemplateResponse? SelectedTemplate { get; private set; }
    public IReadOnlyList<int> PlaceholderNumbers { get; private set; } = Array.Empty<int>();

    /// <summary>The client's pipeline stages in order, labelled (stage filter and badges).</summary>
    public IReadOnlyList<Entities.Dtos.BusinessTypes.PipelineStageOption> Stages { get; private set; } =
        Array.Empty<Entities.Dtos.BusinessTypes.PipelineStageOption>();
    public IReadOnlyList<string> QualificationOptions { get; } = ContactQualification.All.OrderBy(s => s).ToList();
    public IReadOnlyList<string> BookingStatusOptions { get; } = BookingStatus.All.OrderBy(s => s).ToList();

    // SupportsGet so the typed name survives picking a template (which reloads the page with ?WhatsAppTemplateId=).
    [BindProperty(SupportsGet = true)]
    public string Name { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public Guid? WhatsAppTemplateId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    /// <summary>all_eligible | reactivation_no_booking | custom — see CampaignAudienceType.
    /// Defaults to reactivation: it's the flagship option, so a client opening this page for the
    /// first time sees it pre-selected rather than having to notice and pick it.</summary>
    [BindProperty]
    public string AudienceType { get; set; } = CampaignAudienceType.ReactivationNoBooking;

    // "Old contacts who never booked" — deliberately just three simple controls (see class remarks).
    [BindProperty]
    public int InactiveDays { get; set; } = 60;
    [BindProperty]
    public Guid? ReactivationServiceId { get; set; }
    [BindProperty]
    public string? ReactivationSource { get; set; }

    // "Build a custom audience" — common filters (shown immediately)
    [BindProperty]
    public Guid? CustomServiceId { get; set; }
    [BindProperty]
    public int? CustomInactiveDays { get; set; }

    // "Build a custom audience" — advanced filters (behind the expand/collapse section)
    [BindProperty]
    public List<string> CustomStageKeys { get; set; } = new();
    [BindProperty]
    public List<string> CustomQualifications { get; set; } = new();
    [BindProperty]
    public List<string> CustomSources { get; set; } = new();
    [BindProperty]
    public DateTime? CustomCreatedAfter { get; set; }
    [BindProperty]
    public DateTime? CustomCreatedBefore { get; set; }
    [BindProperty]
    public DateTime? CustomLastContactedAfter { get; set; }
    [BindProperty]
    public DateTime? CustomLastContactedBefore { get; set; }
    [BindProperty]
    public List<string> CustomBookingStatuses { get; set; } = new();
    [BindProperty]
    public string? CustomCountry { get; set; }
    [BindProperty]
    public string? CustomCity { get; set; }

    /// <summary>True for the "Select contacts manually" audience choice — still posts AudienceType =
    /// custom (see CampaignAudienceType; there's no fourth backend enum value for it), just with an
    /// explicit ContactIds list instead of AudienceFilters. Kept as its own flag rather than inferred
    /// from ContactIds.Count so the four UI choices map cleanly onto the three backend audience types
    /// without guessing intent from what happens to be checked.</summary>
    [BindProperty]
    public bool ManualSelection { get; set; }

    /// <summary>Which of the FOUR client-facing cards is selected — purely a UI concept (see
    /// Create.cshtml/campaign-audience.js) computed from the two fields actually posted/persisted,
    /// AudienceType and ManualSelection. Used to decide which radio is checked on first render and
    /// on redisplay after a validation error.</summary>
    public string AudienceChoice =>
        AudienceType == CampaignAudienceType.Custom && ManualSelection ? "manual"
        : AudienceType == CampaignAudienceType.Custom ? "custom"
        : AudienceType == CampaignAudienceType.AllEligible ? "all_eligible"
        : "reactivation_no_booking";

    [BindProperty]
    public List<Guid> ContactIds { get; set; } = new();

    [BindProperty]
    public Dictionary<int, string> Variables { get; set; } = new();

    [BindProperty]
    public string SendOption { get; set; } = "now";

    /// <summary>"Schedule" date + time, entered in the VIEWER's own timezone (ViewerTimeZone) and converted to UTC on submit.</summary>
    [BindProperty]
    public DateOnly? ScheduleDate { get; set; }

    [BindProperty]
    public TimeOnly? ScheduleTime { get; set; }

    /// <summary>The viewer's timezone id, shown next to the schedule fields (local-time.js corrects it on first visit).</summary>
    public string ViewerTimezone { get; private set; } = "UTC";

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        // "Pick contacts yourself" only makes sense (and is only reachable in the UI) under "Build a
        // custom audience" — treat it as manual selection regardless in case AudienceType and this
        // checkbox ever disagree (e.g. a replayed/edited form post).
        var isManualSelection = AudienceType == CampaignAudienceType.Custom && ManualSelection;

        if (WhatsAppTemplateId is null || string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = _text[NameAndTemplateKey].Value;
            await LoadAsync(ct);
            return Page();
        }
        if (isManualSelection && ContactIds.Count == 0)
        {
            ErrorMessage = _text[PickSomeoneKey].Value;
            await LoadAsync(ct);
            return Page();
        }
        if (!isManualSelection && Variables.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
        {
            // {ContactFullName}/{ContactFirstName}/{ContactPhone} tokens need the contact list up front to
            // resolve per-recipient — for a filter-resolved audience there's no contact list here yet
            // (it's only resolved inside CampaignService.CreateAsync).
            ErrorMessage = _text[BlanksNeedManualKey].Value;
            await LoadAsync(ct);
            return Page();
        }

        try
        {
            Dictionary<Guid, IReadOnlyList<string>>? variablesByContactId = null;
            var contactIdsForRequest = ContactIds;

            if (isManualSelection)
            {
                // Resolve each recipient's own {{n}} values: a {ContactFullName}/{ContactFirstName}/{ContactPhone}
                // token becomes that contact's own data, anything else is sent as literal text to everyone.
                var selectedContacts = await _contacts.ListAsync(client.Id, stage: null, serviceId: null, search: null, skip: 0, take: 5000, ct);
                var contactsById = selectedContacts.Items.Where(l => ContactIds.Contains(l.Id)).ToDictionary(l => l.Id);

                variablesByContactId = new Dictionary<Guid, IReadOnlyList<string>>();
                var orderedKeys = Variables.Keys.OrderBy(k => k).ToList();
                foreach (var contactId in ContactIds)
                {
                    if (!contactsById.TryGetValue(contactId, out var contact)) continue;
                    variablesByContactId[contactId] = orderedKeys.Select(k => ResolveVariable(Variables[k], contact)).ToList();
                }
            }
            else
            {
                // Audience is resolved server-side from AudienceType/AudienceFilters instead of an
                // explicit list — see ICampaignAudienceService.
                contactIdsForRequest = new List<Guid>();
            }

            var campaignType = AudienceType == CampaignAudienceType.ReactivationNoBooking
                ? CampaignType.Reactivation
                : (string?)null;

            string? audienceFilters = isManualSelection ? null : BuildAudienceFiltersJson(ViewerTimeZone.Resolve(Request, client.Timezone));

            // The schedule is typed as a wall-clock date + time in the viewer's own timezone; convert to UTC
            // (Npgsql timestamptz columns only accept offset 0).
            DateTimeOffset? scheduledAt = null;
            if (SendOption == "later")
            {
                if (ScheduleDate is null || ScheduleTime is null)
                {
                    ErrorMessage = _text[ScheduleMissingKey].Value;
                    await LoadAsync(ct);
                    return Page();
                }
                scheduledAt = ViewerTimeZone.ToUtc(ScheduleDate.Value.ToDateTime(ScheduleTime.Value), ViewerTimeZone.Resolve(Request, client.Timezone));
                if (scheduledAt <= DateTimeOffset.UtcNow)
                {
                    ErrorMessage = _text[SchedulePastKey].Value;
                    await LoadAsync(ct);
                    return Page();
                }
            }

            var campaign = await _campaigns.CreateAsync(new CreateCampaignRequest(
                client.Id, Name, WhatsAppTemplateId.Value, contactIdsForRequest, variablesByContactId, scheduledAt,
                campaignType, AudienceType, audienceFilters), ct);

            if (SendOption == "later" && scheduledAt.HasValue)
            {
                await _campaigns.ScheduleAsync(client.Id, campaign.Id, scheduledAt.Value, ct);
            }
            else if (SendOption == "now")
            {
                await _campaigns.SendAsync(client.Id, campaign.Id, batchSize: 20, ct);
            }

            return RedirectToPage("/Campaigns/Details", new { id = campaign.Id });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = _text.Localize(ex);
            await LoadAsync(ct);
            return Page();
        }
    }

    private static string ResolveVariable(string? raw, ContactResponse contact) => raw switch
    {
        "{ContactFullName}" => contact.FullName ?? "",
        "{ContactFirstName}" => contact.FirstName ?? contact.FullName ?? "",
        "{ContactPhone}" => contact.Phone ?? "",
        _ => raw ?? ""
    };

    /// <summary>Builds the AudienceFilters JSON from whichever set of bound properties applies to
    /// the currently selected AudienceType — reactivation's three simple controls, or the custom
    /// builder's common + advanced fields. Shares one CampaignAudienceFilters shape for both (see
    /// its remarks) since CampaignAudienceService already only reads the subset relevant to each
    /// audience type.</summary>
    private string? BuildAudienceFiltersJson(TimeZoneInfo viewerTz)
    {
        CampaignAudienceFilters filters;
        if (AudienceType == CampaignAudienceType.ReactivationNoBooking)
        {
            filters = new CampaignAudienceFilters(
                InactiveDays: InactiveDays,
                ServiceId: ReactivationServiceId,
                Sources: string.IsNullOrWhiteSpace(ReactivationSource) ? null : new[] { ReactivationSource });
        }
        else if (AudienceType == CampaignAudienceType.Custom)
        {
            filters = new CampaignAudienceFilters(
                InactiveDays: CustomInactiveDays,
                ServiceId: CustomServiceId,
                StageKeys: CustomStageKeys.Count > 0 ? CustomStageKeys : null,
                Sources: CustomSources.Count > 0 ? CustomSources : null,
                Qualifications: CustomQualifications.Count > 0 ? CustomQualifications : null,
                CreatedAfter: LocalMidnightUtc(CustomCreatedAfter, viewerTz),
                CreatedBefore: LocalMidnightUtc(CustomCreatedBefore?.AddDays(1), viewerTz),
                LastContactedAfter: LocalMidnightUtc(CustomLastContactedAfter, viewerTz),
                LastContactedBefore: LocalMidnightUtc(CustomLastContactedBefore?.AddDays(1), viewerTz),
                BookingStatuses: CustomBookingStatuses.Count > 0 ? CustomBookingStatuses : null,
                Countries: string.IsNullOrWhiteSpace(CustomCountry) ? null : new[] { CustomCountry },
                Cities: string.IsNullOrWhiteSpace(CustomCity) ? null : new[] { CustomCity });
        }
        else
        {
            return null; // all_eligible — mandatory exclusions only, no further filters
        }

        return JsonSerializer.Serialize(filters, AudienceFiltersJsonOptions);
    }

    /// <summary>The date filters are whole days in the viewer's timezone: midnight there, as UTC. The "to" date is
    /// inclusive, so callers pass the day after it.</summary>
    private static DateTimeOffset? LocalMidnightUtc(DateTime? date, TimeZoneInfo tz) =>
        date is null ? null : ViewerTimeZone.ToUtc(date.Value.Date, tz);

    private async Task LoadAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null)
        {
            ClientConfigured = false;
            return;
        }

        ClientConfigured = true;
        ViewerTimezone = ViewerTimeZone.Resolve(Request, client.Timezone).Id;

        var allTemplates = await _templates.ListAsync(client.Id, ct);
        ApprovedTemplates = allTemplates.Where(t => t.Status == WhatsAppTemplateStatus.Approved).ToList();

        if (WhatsAppTemplateId.HasValue)
        {
            SelectedTemplate = ApprovedTemplates.FirstOrDefault(t => t.Id == WhatsAppTemplateId.Value);
            if (SelectedTemplate is not null)
            {
                PlaceholderNumbers = PlaceholderPattern.Matches(SelectedTemplate.Body)
                    .Select(m => int.Parse(m.Groups[1].Value))
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();
            }
        }

        var (items, _) = await _contacts.ListAsync(client.Id, stage: null, serviceId: null, Search, skip: 0, take: 200, ct);
        Contacts = items;

        Services = await _services.ListAsync(client.Id, activeOnly: true, ct);
        ContactSources = await _contacts.GetDistinctSourcesAsync(client.Id, ct);
        Stages = await _contacts.ListStagesAsync(client.Id, ct);
    }
}
