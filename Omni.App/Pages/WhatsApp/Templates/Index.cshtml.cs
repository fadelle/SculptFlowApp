using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.WhatsApp;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Requests.WhatsApp;
using Omni.App.Entities.Responses.WhatsApp;

namespace Omni.App.Pages.WhatsApp.Templates;

/// <summary>WhatsApp Templates: list existing templates (with Meta approval status) and submit new
/// ones for review. Actual Meta API calls live in WhatsAppTemplateService — this page just collects
/// the form and reports back whatever the service (and, through it, Meta) returned.</summary>
public class IndexModel : PageModel
{
    private const string SavedKey = "templates.saved";
    private const string SubmitFailedKey = "templates.submitFailed";
    private const string DuplicateKey = "templates.errors.duplicate";
    private const string RefreshedKey = "templates.refreshed";
    private const string RetryRejectedKey = "templates.retryRejected";
    private const string RetrySentKey = "templates.retrySent";

    private readonly ICurrentClientContext _clientContext;
    private readonly IWhatsAppTemplateService _templates;
    private readonly IStringLocalizer _text;

    public IndexModel(ICurrentClientContext clientContext, IWhatsAppTemplateService templates, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _templates = templates;
        _text = text;
    }

    public bool ClientConfigured { get; private set; }
    public Guid ClientId { get; private set; }
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

    /// <summary>True when a new template failed validation: the page shows the popup again with the typed values.</summary>
    public bool ReopenNewTemplate { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        try
        {
            var template = await _templates.CreateAsync(new CreateWhatsAppTemplateRequest(
                client.Id, Name, Category, Language, Body,
                HeaderType == WhatsAppTemplateHeaderType.None ? null : HeaderType,
                HeaderContent, Footer, ButtonsJson: null, VariablesJson: null), ct);

            StatusMessage = template.Status == WhatsAppTemplateStatus.Rejected
                ? null
                : _text[SavedKey, template.Name, _text.TemplateStatusLabel(template.Status)].Value;
            ErrorMessage = template.Status == WhatsAppTemplateStatus.Rejected
                ? _text[SubmitFailedKey, template.Name, template.RejectionReason ?? string.Empty].Value
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or DuplicateRecordException)
        {
            // Nothing was saved: show the form again with what was typed, instead of redirecting it away. A duplicate is the
            // unique (client, name, language) index: the client already has a template with this name.
            ErrorMessage = ex is DuplicateRecordException
                ? _text[DuplicateKey, Name].Value
                : _text.Localize(ex);
            ReopenNewTemplate = true;
            await LoadAsync(ct);
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSyncAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        try
        {
            var template = await _templates.SyncStatusAsync(client.Id, id, ct);
            StatusMessage = template is null ? null : _text[RefreshedKey, template.Name, _text.TemplateStatusLabel(template.Status)].Value;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = _text.Localize(ex);
        }
        catch (MetaGraphApiException ex)
        {
            ErrorMessage = _text.Localize(ex);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRetryAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();

        try
        {
            var template = await _templates.RetrySubmitAsync(client.Id, id, ct);
            if (template is not null)
            {
                if (template.Status == WhatsAppTemplateStatus.Rejected)
                    ErrorMessage = _text[RetryRejectedKey, template.Name, template.RejectionReason ?? string.Empty].Value;
                else
                    StatusMessage = _text[RetrySentKey, template.Name].Value;
            }
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = _text.Localize(ex);
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null)
        {
            ClientConfigured = false;
            return;
        }

        ClientConfigured = true;
        ClientId = client.Id;
        Templates = await _templates.ListAsync(client.Id, ct);
    }
}
