using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Campaigns;

/// <summary>
/// Campaign creation: pick an approved template, pick an audience, map body variables, and choose
/// send-now vs schedule-later.
///
/// Audience is presented as three business-framed choices — "Old leads who never booked"
/// (AudienceType.ReactivationNoConsultation, the flagship reactivation feature), "All contactable
/// leads" (AllEligible), and "Build a custom audience" (Custom, with a progressive-disclosure
/// filter builder: a couple of common fields up front, the rest behind "Advanced filters" — see
/// Create.cshtml). Internal enum values (all_eligible, reactivation_no_consultation, custom) never
/// reach the UI text, only the radio `value`s wwwroot/js/campaign-audience.js reads.
///
/// "Build a custom audience" also has a "pick leads yourself" escape hatch (ManualSelection) that
/// reveals the original search + checkbox list — still audience_type = custom under the hood, just
/// with an explicit LeadIds list instead of AudienceFilters (see CampaignService.CreateAsync: an
/// explicit list always wins over filters when both could apply).
///
/// See wwwroot/js/campaign-audience.js for the live match-count preview and ICampaignAudienceService
/// for how each audience actually resolves to a lead list. Each variable box may contain literal
/// text or a {LeadX} token, resolved per-lead before calling ICampaignService.CreateAsync — but
/// that per-lead resolution needs to know the lead list up front, so it's only available for the
/// manual selection (see OnPostAsync).
/// </summary>
public class CreateModel : PageModel
{
    private static readonly Regex PlaceholderPattern = new(@"\{\{(\d+)\}\}", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions AudienceFiltersJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICurrentClinicContext _clinicContext;
    private readonly IWhatsAppTemplateService _templates;
    private readonly ILeadService _leads;
    private readonly IProcedureService _procedures;
    private readonly ICampaignService _campaigns;

    public CreateModel(ICurrentClinicContext clinicContext, IWhatsAppTemplateService templates, ILeadService leads, IProcedureService procedures, ICampaignService campaigns)
    {
        _clinicContext = clinicContext;
        _templates = templates;
        _leads = leads;
        _procedures = procedures;
        _campaigns = campaigns;
    }

    public bool ClinicConfigured { get; private set; }
    public IReadOnlyList<WhatsAppTemplateResponse> ApprovedTemplates { get; private set; } = Array.Empty<WhatsAppTemplateResponse>();
    public IReadOnlyList<LeadResponse> Leads { get; private set; } = Array.Empty<LeadResponse>();
    public IReadOnlyList<ProcedureResponse> Procedures { get; private set; } = Array.Empty<ProcedureResponse>();
    public IReadOnlyList<string> LeadSources { get; private set; } = Array.Empty<string>();
    public WhatsAppTemplateResponse? SelectedTemplate { get; private set; }
    public IReadOnlyList<int> PlaceholderNumbers { get; private set; } = Array.Empty<int>();

    public IReadOnlyList<string> LeadStatusOptions { get; } = LeadStatus.All.OrderBy(s => s).ToList();
    public IReadOnlyList<string> QualificationStatusOptions { get; } = LeadQualificationStatus.All.OrderBy(s => s).ToList();
    public IReadOnlyList<string> AppointmentStatusOptions { get; } = AppointmentStatus.All.OrderBy(s => s).ToList();

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public Guid? WhatsAppTemplateId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    /// <summary>all_eligible | reactivation_no_consultation | custom — see CampaignAudienceType.
    /// Defaults to reactivation: it's the flagship option, so a clinic opening this page for the
    /// first time sees it pre-selected rather than having to notice and pick it.</summary>
    [BindProperty]
    public string AudienceType { get; set; } = CampaignAudienceType.ReactivationNoConsultation;

    // "Old leads who never booked" — deliberately just three simple controls (see class remarks).
    [BindProperty]
    public int InactiveDays { get; set; } = 60;
    [BindProperty]
    public Guid? ReactivationProcedureId { get; set; }
    [BindProperty]
    public string? ReactivationSource { get; set; }

    // "Build a custom audience" — common filters (shown immediately)
    [BindProperty]
    public Guid? CustomProcedureId { get; set; }
    [BindProperty]
    public int? CustomInactiveDays { get; set; }

    // "Build a custom audience" — advanced filters (behind the expand/collapse section)
    [BindProperty]
    public List<string> CustomLeadStatuses { get; set; } = new();
    [BindProperty]
    public List<string> CustomQualificationStatuses { get; set; } = new();
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
    public List<string> CustomAppointmentStatuses { get; set; } = new();
    [BindProperty]
    public string? CustomCountry { get; set; }
    [BindProperty]
    public string? CustomCity { get; set; }

    /// <summary>True for the "Select leads manually" audience choice — still posts AudienceType =
    /// custom (see CampaignAudienceType; there's no fourth backend enum value for it), just with an
    /// explicit LeadIds list instead of AudienceFilters. Kept as its own flag rather than inferred
    /// from LeadIds.Count so the four UI choices map cleanly onto the three backend audience types
    /// without guessing intent from what happens to be checked.</summary>
    [BindProperty]
    public bool ManualSelection { get; set; }

    /// <summary>Which of the FOUR clinic-facing cards is selected — purely a UI concept (see
    /// Create.cshtml/campaign-audience.js) computed from the two fields actually posted/persisted,
    /// AudienceType and ManualSelection. Used to decide which radio is checked on first render and
    /// on redisplay after a validation error.</summary>
    public string AudienceChoice =>
        AudienceType == CampaignAudienceType.Custom && ManualSelection ? "manual"
        : AudienceType == CampaignAudienceType.Custom ? "custom"
        : AudienceType == CampaignAudienceType.AllEligible ? "all_eligible"
        : "reactivation_no_consultation";

    [BindProperty]
    public List<Guid> LeadIds { get; set; } = new();

    [BindProperty]
    public Dictionary<int, string> Variables { get; set; } = new();

    [BindProperty]
    public string SendOption { get; set; } = "now";

    [BindProperty]
    public DateTimeOffset? ScheduledAt { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        // "Pick leads yourself" only makes sense (and is only reachable in the UI) under "Build a
        // custom audience" — treat it as manual selection regardless in case AudienceType and this
        // checkbox ever disagree (e.g. a replayed/edited form post).
        var isManualSelection = AudienceType == CampaignAudienceType.Custom && ManualSelection;

        if (WhatsAppTemplateId is null || string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Name and a template are required.";
            await LoadAsync(ct);
            return Page();
        }
        if (isManualSelection && LeadIds.Count == 0)
        {
            ErrorMessage = "Pick at least one recipient, or switch back to an audience filter.";
            await LoadAsync(ct);
            return Page();
        }
        if (!isManualSelection && Variables.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
        {
            // {LeadFullName}/{LeadFirstName}/{LeadPhone} tokens need the lead list up front to
            // resolve per-recipient — for a filter-resolved audience there's no lead list here yet
            // (it's only resolved inside CampaignService.CreateAsync).
            ErrorMessage = "Body variables aren't supported yet for audience-based campaigns — pick leads yourself to use them, or pick a template with no variables.";
            await LoadAsync(ct);
            return Page();
        }

        try
        {
            Dictionary<Guid, IReadOnlyList<string>>? variablesByLeadId = null;
            var leadIdsForRequest = LeadIds;

            if (isManualSelection)
            {
                // Resolve each recipient's own {{n}} values: a {LeadFullName}/{LeadFirstName}/{LeadPhone}
                // token becomes that lead's own data, anything else is sent as literal text to everyone.
                var selectedLeads = await _leads.ListAsync(clinic.Id, status: null, procedureId: null, search: null, skip: 0, take: 5000, ct);
                var leadsById = selectedLeads.Items.Where(l => LeadIds.Contains(l.Id)).ToDictionary(l => l.Id);

                variablesByLeadId = new Dictionary<Guid, IReadOnlyList<string>>();
                var orderedKeys = Variables.Keys.OrderBy(k => k).ToList();
                foreach (var leadId in LeadIds)
                {
                    if (!leadsById.TryGetValue(leadId, out var lead)) continue;
                    variablesByLeadId[leadId] = orderedKeys.Select(k => ResolveVariable(Variables[k], lead)).ToList();
                }
            }
            else
            {
                // Audience is resolved server-side from AudienceType/AudienceFilters instead of an
                // explicit list — see ICampaignAudienceService.
                leadIdsForRequest = new List<Guid>();
            }

            var campaignType = AudienceType == CampaignAudienceType.ReactivationNoConsultation
                ? CampaignType.Reactivation
                : (string?)null;

            string? audienceFilters = isManualSelection ? null : BuildAudienceFiltersJson();

            // datetime-local has no offset, so model binding attaches the server's local offset —
            // Npgsql's timestamptz columns only accept UTC (offset 0), so normalize here. Pre-existing
            // gap (not introduced by the audience picker), found while testing "Send later" above.
            var scheduledAt = SendOption == "later" && ScheduledAt.HasValue ? ScheduledAt.Value.ToUniversalTime() : (DateTimeOffset?)null;

            var campaign = await _campaigns.CreateAsync(new CreateCampaignRequest(
                clinic.Id, Name, WhatsAppTemplateId.Value, leadIdsForRequest, variablesByLeadId, scheduledAt,
                campaignType, AudienceType, audienceFilters), ct);

            if (SendOption == "later" && scheduledAt.HasValue)
            {
                await _campaigns.ScheduleAsync(clinic.Id, campaign.Id, scheduledAt.Value, ct);
            }
            else if (SendOption == "now")
            {
                await _campaigns.SendAsync(clinic.Id, campaign.Id, batchSize: 20, ct);
            }

            return RedirectToPage("/Campaigns/Details", new { id = campaign.Id });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = ex.Message;
            await LoadAsync(ct);
            return Page();
        }
    }

    private static string ResolveVariable(string? raw, LeadResponse lead) => raw switch
    {
        "{LeadFullName}" => lead.FullName ?? "",
        "{LeadFirstName}" => lead.FirstName ?? lead.FullName ?? "",
        "{LeadPhone}" => lead.Phone ?? "",
        _ => raw ?? ""
    };

    /// <summary>Builds the AudienceFilters JSON from whichever set of bound properties applies to
    /// the currently selected AudienceType — reactivation's three simple controls, or the custom
    /// builder's common + advanced fields. Shares one CampaignAudienceFilters shape for both (see
    /// its remarks) since CampaignAudienceService already only reads the subset relevant to each
    /// audience type.</summary>
    private string? BuildAudienceFiltersJson()
    {
        CampaignAudienceFilters filters;
        if (AudienceType == CampaignAudienceType.ReactivationNoConsultation)
        {
            filters = new CampaignAudienceFilters(
                InactiveDays: InactiveDays,
                ProcedureId: ReactivationProcedureId,
                Sources: string.IsNullOrWhiteSpace(ReactivationSource) ? null : new[] { ReactivationSource });
        }
        else if (AudienceType == CampaignAudienceType.Custom)
        {
            filters = new CampaignAudienceFilters(
                InactiveDays: CustomInactiveDays,
                ProcedureId: CustomProcedureId,
                LeadStatuses: CustomLeadStatuses.Count > 0 ? CustomLeadStatuses : null,
                Sources: CustomSources.Count > 0 ? CustomSources : null,
                QualificationStatuses: CustomQualificationStatuses.Count > 0 ? CustomQualificationStatuses : null,
                CreatedAfter: ToUtcOffset(CustomCreatedAfter),
                CreatedBefore: ToUtcOffset(CustomCreatedBefore),
                LastContactedAfter: ToUtcOffset(CustomLastContactedAfter),
                LastContactedBefore: ToUtcOffset(CustomLastContactedBefore),
                AppointmentStatuses: CustomAppointmentStatuses.Count > 0 ? CustomAppointmentStatuses : null,
                Countries: string.IsNullOrWhiteSpace(CustomCountry) ? null : new[] { CustomCountry },
                Cities: string.IsNullOrWhiteSpace(CustomCity) ? null : new[] { CustomCity });
        }
        else
        {
            return null; // all_eligible — mandatory exclusions only, no further filters
        }

        return JsonSerializer.Serialize(filters, AudienceFiltersJsonOptions);
    }

    private static DateTimeOffset? ToUtcOffset(DateTime? value) =>
        value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null;

    private async Task LoadAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            ClinicConfigured = false;
            return;
        }

        ClinicConfigured = true;

        var allTemplates = await _templates.ListAsync(clinic.Id, ct);
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

        var (items, _) = await _leads.ListAsync(clinic.Id, status: null, procedureId: null, Search, skip: 0, take: 200, ct);
        Leads = items;

        Procedures = await _procedures.ListAsync(clinic.Id, activeOnly: true, ct);
        LeadSources = await _leads.GetDistinctSourcesAsync(clinic.Id, ct);
    }
}
