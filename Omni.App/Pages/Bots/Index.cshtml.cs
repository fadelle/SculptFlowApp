using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Ai;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Ai;

namespace Omni.App.Pages.Bots;

/// <summary>Bots: the client's AI bots (IBotService). "New bot" offers the three templates and a custom bot; each is created
/// as a draft and opened in the editor. Activate / deactivate, "Use as Custom Bot" and delete act from the list. Where a bot
/// answers is set on Settings → Inbox settings.</summary>
public class IndexModel : PageModel
{
    private const string CopyNameKey = "bots.copyName";
    private const string ActivatedKey = "bots.done.activated";
    private const string DeactivatedKey = "bots.done.deactivated";
    private const string DeletedKey = "bots.done.deleted";
    private const string CopiedKey = "bots.done.copied";
    private const string NotFoundKey = "bots.errors.notFound";

    private readonly ICurrentClientContext _clientContext;
    private readonly IBotService _bots;
    private readonly IStringLocalizer _text;

    public IndexModel(ICurrentClientContext clientContext, IBotService bots, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _bots = bots;
        _text = text;
    }

    public bool ClientConfigured { get; private set; }
    public BotListDto? List { get; private set; }

    [TempData] public string? StatusMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return;
        ClientConfigured = true;
        List = await _bots.ListAsync(client.Id, ct);
    }

    public async Task<IActionResult> OnPostCreateAsync(string template, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();
        try
        {
            var bot = await _bots.CreateAsync(client.Id, template, _text[BotTextKeys.TemplateName(template)].Value, ct);
            return RedirectToPage("/Bots/Edit", new { id = bot.Id });
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = _text.Localize(ex);
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostStatusAsync(Guid id, string status, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();
        try
        {
            var bot = await _bots.SetStatusAsync(client.Id, id, status, ct);
            StatusMessage = _text[bot.Status == BotStatus.Active ? ActivatedKey : DeactivatedKey, bot.Name].Value;
        }
        catch (KeyNotFoundException)
        {
            ErrorMessage = _text[NotFoundKey].Value;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // InvalidOperationException covers a bot that isn't ready and EntitlementDeniedException (plan limits).
            ErrorMessage = _text.Localize(ex);
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDuplicateAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();
        var source = await _bots.GetAsync(client.Id, id, ct);
        if (source is null)
        {
            ErrorMessage = _text[NotFoundKey].Value;
            return RedirectToPage();
        }
        var copy = await _bots.DuplicateAsCustomAsync(client.Id, id, CopyName(source.Name), ct);
        StatusMessage = _text[CopiedKey, source.Name].Value;
        return RedirectToPage("/Bots/Edit", new { id = copy.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();
        var bot = await _bots.GetAsync(client.Id, id, ct);
        if (bot is null)
        {
            ErrorMessage = _text[NotFoundKey].Value;
            return RedirectToPage();
        }
        await _bots.DeleteAsync(client.Id, id, ct);
        StatusMessage = _text[DeletedKey, bot.Name].Value;
        return RedirectToPage();
    }

    /// <summary>"{name} (custom)", cut to the 100-character name limit.</summary>
    private string CopyName(string name)
    {
        var copy = _text[CopyNameKey, name].Value;
        return copy.Length <= 100 ? copy : copy[..100];
    }
}
