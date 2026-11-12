using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Contacts;
using Omni.App.Business.Services.Contacts;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Contacts;

namespace Omni.App.Pages.Dashboard;

/// <summary>Contacts → Import (QA t12-fix-05), in three steps on one page: upload a CSV; check the columns guessed for each
/// field and the first rows (the file travels back in a hidden field, nothing is saved yet); import and read the summary
/// (added, updated, skipped, and each refused row with its line and why). Any member may import.</summary>
[RequestSizeLimit(4 * 1024 * 1024)]
public class ContactsImportModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IContactImportService _import;
    private readonly IStringLocalizer _text;

    public ContactsImportModel(ICurrentClientContext clientContext, IContactImportService import, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _import = import;
        _text = text;
    }

    public bool ClientConfigured { get; private set; }
    public string? ErrorMessage { get; private set; }
    public ContactImportPreview? Preview { get; private set; }
    public ContactImportResult? Result { get; private set; }

    /// <summary>The file's text between the preview and the import.</summary>
    [BindProperty] public string? Csv { get; set; }
    /// <summary>Field → column index (-1 = not imported).</summary>
    [BindProperty] public Dictionary<string, int> Mapping { get; set; } = new();
    [BindProperty] public bool UpdateExisting { get; set; }

    public int MaxRows => ContactImportService.MaxRows;
    public int MaxKilobytes => ContactImportService.MaxBytes / 1024;

    public async Task OnGetAsync(CancellationToken ct) => ClientConfigured = await _clientContext.GetClientAsync(ct) is not null;

    public async Task<IActionResult> OnPostPreviewAsync(IFormFile? file, CancellationToken ct)
    {
        if (await _clientContext.GetClientAsync(ct) is null) return RedirectToPage();
        ClientConfigured = true;
        try
        {
            if (file is null || file.Length == 0) throw new ArgumentException("contacts.import.errors.empty");
            if (file.Length > ContactImportService.MaxBytes) throw new ArgumentException("contacts.import.errors.tooBig").WithArgs(MaxKilobytes);
            using var reader = new StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            Csv = await reader.ReadToEndAsync(ct);
            Preview = _import.Preview(Csv);
            Mapping = Preview.Mapping.ToDictionary(m => m.Key, m => m.Value);
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = _text.Localize(ex);
            Csv = null;
            Preview = null;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostImportAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();
        ClientConfigured = true;
        try
        {
            var mapping = Mapping.Where(m => m.Value >= 0 && ContactImportField.All.Contains(m.Key)).ToDictionary(m => m.Key, m => m.Value);
            Result = await _import.ImportAsync(client.Id, Csv ?? "", mapping, UpdateExisting, ct);
            Csv = null;
        }
        catch (ArgumentException ex)
        {
            // The column choice stays on screen to fix; a file that can't be read starts over.
            ErrorMessage = _text.Localize(ex);
            try
            {
                Preview = _import.Preview(Csv ?? "");
            }
            catch (ArgumentException)
            {
                Csv = null;
            }
        }
        return Page();
    }
}
