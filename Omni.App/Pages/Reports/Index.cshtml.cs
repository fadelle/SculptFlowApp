using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Business.Contracts.Services.Reports;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.BusinessTypes;
using Omni.App.Entities.Responses.Reports;

namespace Omni.App.Pages.Reports;

/// <summary>Reports: the client's own numbers for a period (the Dashboard's period picker, in the viewer's time). The basic
/// sections need the plan's <c>reporting</c> feature (Growth and up), the advanced ones <c>advanced_reporting</c> (AI Pro,
/// Business); whatever the plan lacks is left out, and without reporting the page says it isn't included.</summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IEntitlementService _entitlements;
    private readonly IReportService _reports;
    private readonly IContactService _contacts;
    private readonly IConfigManager _settings;
    private readonly IStringLocalizer _text;

    public IndexModel(ICurrentClientContext clientContext, IEntitlementService entitlements, IReportService reports,
        IContactService contacts, IConfigManager settings, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _entitlements = entitlements;
        _reports = reports;
        _contacts = contacts;
        _settings = settings;
        _text = text;
    }

    [BindProperty(SupportsGet = true)]
    public string Period { get; set; } = ReportPeriod.ThisMonth;

    /// <summary>Custom range, inclusive, as viewer-local dates.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    public bool ClientConfigured { get; private set; }

    /// <summary>False when the plan has no reporting: the page shows that instead of the reports.</summary>
    public bool ReportingIncluded { get; private set; }

    public string RangeLabel { get; private set; } = string.Empty;
    public BasicReportResponse? Basic { get; private set; }

    /// <summary>Null when the plan has no advanced reporting.</summary>
    public AdvancedReportResponse? Advanced { get; private set; }

    /// <summary>Charged usage is shown only while billing is on.</summary>
    public bool ShowUsage { get; private set; }

    public IReadOnlyList<PipelineStageOption> Stages { get; private set; } = Array.Empty<PipelineStageOption>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        ClientConfigured = client is not null;
        if (client is null) return;

        var plan = await _entitlements.GetAsync(client.Id, ct);
        ReportingIncluded = plan.CanUseReporting;
        if (!ReportingIncluded) return;

        var tz = ViewerTimeZone.Resolve(Request, client.Timezone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).Date);
        (Period, var from, var to) = ReportPeriod.Resolve(Period, From, To, today);
        RangeLabel = ReportPeriod.Label(_text, from, to);

        Basic = await _reports.GetBasicAsync(client.Id, from, to, tz, ct);
        Stages = await _contacts.ListStagesAsync(client.Id, ct);
        if (plan.CanUseAdvancedReporting)
        {
            Advanced = await _reports.GetAdvancedAsync(client.Id, from, to, tz, ct);
            ShowUsage = _settings.BillingEnabled;
        }
    }
}
