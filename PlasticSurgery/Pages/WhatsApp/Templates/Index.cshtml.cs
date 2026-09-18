using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Data.Entities;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.WhatsApp.Templates;

/// <summary>WhatsApp Templates: list existing templates (with Meta approval status) and submit new
/// ones for review. Actual Meta API calls live in WhatsAppTemplateService — this page just collects
/// the form and reports back whatever the service (and, through it, Meta) returned.</summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IWhatsAppTemplateService _templates;

    public IndexModel(ICurrentClinicContext clinicContext, IWhatsAppTemplateService templates)
    {
        _clinicContext = clinicContext;
        _templates = templates;
    }

    public bool ClinicConfigured { get; private set; }
    public Guid ClinicId { get; private set; }
    public IReadOnlyList<WhatsAppTemplateResponse> Templates { get; private set; } = Array.Empty<WhatsAppTemplateResponse>();
    public IReadOnlyList<string> Categories { get; } = WhatsAppTemplateCategory.All.OrderBy(c => c).ToList();
    public IReadOnlyList<string> HeaderTypes { get; } = WhatsAppTemplateHeaderType.All.OrderBy(h => h).ToList();

    [BindProperty]
    public string Name { get; set; } = string.Empty;

    [BindProperty]
    public string Category { get; set; } = WhatsAppTemplateCategory.Utility;

    [BindProperty]
    public string Language { get; set; } = "en_US";

    [BindProperty]
    public string HeaderType { get; set; } = WhatsAppTemplateHeaderType.None;

    [BindProperty]
    public string? HeaderContent { get; set; }

    [BindProperty]
    public string Body { get; set; } = string.Empty;

    [BindProperty]
    public string? Footer { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        try
        {
            var template = await _templates.CreateAsync(new CreateWhatsAppTemplateRequest(
                clinic.Id, Name, Category, Language, Body,
                HeaderType == WhatsAppTemplateHeaderType.None ? null : HeaderType,
                HeaderContent, Footer, ButtonsJson: null, VariablesJson: null), ct);

            StatusMessage = template.Status == WhatsAppTemplateStatus.Rejected
                ? null
                : $"Template '{template.Name}' saved — status: {template.Status}.";
            ErrorMessage = template.Status == WhatsAppTemplateStatus.Rejected
                ? $"Template '{template.Name}' was saved as a draft, but Meta submission failed: {template.RejectionReason}"
                : null;
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSyncAsync(Guid id, CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null) return RedirectToPage();

        try
        {
            var template = await _templates.SyncStatusAsync(clinic.Id, id, ct);
            StatusMessage = template is null ? null : $"'{template.Name}' status refreshed: {template.Status}.";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (MetaGraphApiException ex)
        {
            ErrorMessage = ex.Message;
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            ClinicConfigured = false;
            return;
        }

        ClinicConfigured = true;
        ClinicId = clinic.Id;
        Templates = await _templates.ListAsync(clinic.Id, ct);
    }
}
