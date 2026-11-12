using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.BusinessTypes;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.BusinessTypes;

namespace Omni.App.Pages.Settings;

/// <summary>Settings → Contact stages (QA t12-fix-06): the client's own name for each stage of its business type, per language.
/// The stages, their order and categories stay the type's (reports, campaign filters and automatic moves read those); an empty
/// box uses the type's name. Any member may change them.</summary>
public class ContactStagesModel : PageModel
{
    private const string SavedKey = "contactStages.saved";

    private readonly ICurrentClientContext _clientContext;
    private readonly IBusinessTypeService _types;
    private readonly IStringLocalizer _text;

    public ContactStagesModel(ICurrentClientContext clientContext, IBusinessTypeService types, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _types = types;
        _text = text;
    }

    /// <summary>One stage's boxes, posted as Rows[i].*.</summary>
    public sealed class StageForm
    {
        public string Key { get; set; } = string.Empty;
        public Dictionary<string, string?> Labels { get; set; } = new();
    }

    public bool ClientConfigured { get; private set; }
    public IReadOnlyList<ClientStageLabelRow> Stages { get; private set; } = [];
    [BindProperty] public List<StageForm> Rows { get; set; } = [];
    [TempData] public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; private set; }

    public static IReadOnlyList<string> Languages => AppLanguage.All;

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (await LoadAsync(ct) is false) return;
        Rows = Stages.Select(s => new StageForm
        {
            Key = s.Key,
            Labels = Languages.ToDictionary(l => l, l => s.OwnLabels.TryGetValue(l, out var v) ? v : null)
        }).ToList();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();
        try
        {
            await _types.SaveClientStageLabelsAsync(client.Id,
                Rows.Where(r => !string.IsNullOrEmpty(r.Key)).ToDictionary(r => r.Key,
                    r => r.Labels.Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToDictionary(l => l.Key, l => l.Value!)), ct);
            StatusMessage = _text[SavedKey].Value;
            return RedirectToPage();
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = _text.Localize(ex);
        }
        await LoadAsync(ct); // the posted boxes stay as typed
        return Page();
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return false;
        ClientConfigured = true;
        Stages = await _types.ListClientStageLabelsAsync(client.Id, ct);
        return true;
    }
}
