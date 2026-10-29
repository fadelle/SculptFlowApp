using Omni.App.Controllers.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Ai;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Entities.Requests.Ai;

namespace Omni.App.Pages.Bots;

/// <summary>The bot editor (IBotService.SaveAsync): name, description, instructions and allowed actions (custom bots only; a
/// template bot shows its locked ones), Knowledge Base sources, language, handoff policy and message, status; and a test
/// conversation panel (wwwroot/js/bot-test.js, BotTestController) that runs the bot with simulated write actions.</summary>
[RequirePlanFeature(EntitlementKeys.AiAssistant)]
public class EditModel : PageModel
{
    private const string SavedKey = "bots.done.saved";
    private const string CopiedKey = "bots.done.copied";
    private const string CopyNameKey = "bots.copyName";
    private const string DeletedKey = "bots.done.deleted";

    private readonly ICurrentClientContext _clientContext;
    private readonly IBotService _bots;
    private readonly IConfigManager _config;
    private readonly IStringLocalizer _text;

    public EditModel(ICurrentClientContext clientContext, IBotService bots, IConfigManager config, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _bots = bots;
        _config = config;
        _text = text;
    }

    public bool ClientConfigured { get; private set; }
    public BotDto? Bot { get; private set; }
    public IReadOnlyList<BotKnowledgeChoice> KnowledgeChoices { get; private set; } = [];
    public bool PlanIncludesAi { get; private set; }

    /// <summary>What still stops the bot from running (a text key), shown over the test panel; null when it is ready.</summary>
    public string? NotReadyKey { get; private set; }

    public int InstructionsMaxLength => _config.BotsInstructionsMaxLength;
    public int HandoffMessageMaxLength => _config.BotsHandoffMessageMaxLength;
    public int TestMessageMaxLength => _config.BotsTestMessageMaxLength;

    [BindProperty] public string? Name { get; set; }
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public string? Instructions { get; set; }
    [BindProperty] public List<string> Capabilities { get; set; } = [];
    [BindProperty] public string KnowledgeScope { get; set; } = BotKnowledgeScope.All;
    [BindProperty] public List<Guid> KnowledgeDocumentIds { get; set; } = [];
    [BindProperty] public List<Guid> WebsiteSourceIds { get; set; } = [];
    [BindProperty] public string? Language { get; set; }
    [BindProperty] public string HandoffPolicy { get; set; } = BotHandoffPolicy.OnRequestOrUnsure;
    [BindProperty] public string? HandoffMessage { get; set; }
    [BindProperty] public string Status { get; set; } = BotStatus.Draft;

    [TempData] public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        if (!await LoadAsync(id, ct)) return Bot is null && ClientConfigured ? NotFound() : Page();
        Fill(Bot!);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();
        try
        {
            var saved = await _bots.SaveAsync(client.Id, id, new SaveBotRequest(Name, Description, Instructions, Capabilities, KnowledgeScope,
                KnowledgeDocumentIds, WebsiteSourceIds, string.IsNullOrWhiteSpace(Language) ? null : Language, HandoffPolicy, HandoffMessage, Status), ct);
            StatusMessage = _text[SavedKey, saved.Name].Value;
            return RedirectToPage(new { id });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Redisplay what was typed with the reason (InvalidOperationException: not ready to activate, or a plan limit).
            ErrorMessage = _text.Localize(ex);
            if (!await LoadAsync(id, ct)) return NotFound();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDuplicateAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage();
        var source = await _bots.GetAsync(client.Id, id, ct);
        if (source is null) return NotFound();
        var name = _text[CopyNameKey, source.Name].Value;
        var copy = await _bots.DuplicateAsCustomAsync(client.Id, id, name.Length <= 100 ? name : name[..100], ct);
        StatusMessage = _text[CopiedKey, source.Name].Value;
        return RedirectToPage(new { id = copy.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return RedirectToPage("/Bots/Index");
        var bot = await _bots.GetAsync(client.Id, id, ct);
        if (bot is null) return NotFound();
        await _bots.DeleteAsync(client.Id, id, ct);
        StatusMessage = _text[DeletedKey, bot.Name].Value;
        return RedirectToPage("/Bots/Index");
    }

    /// <summary>The languages the form offers for a fixed reply language.</summary>
    public IEnumerable<string> Languages => BotLanguages.Codes;

    /// <summary>True when the bot's form value <paramref name="capability"/> is on (custom) or the template has it.</summary>
    public bool HasCapability(string capability) => Bot is { IsTemplate: true } bot ? bot.Capabilities.Contains(capability) : Capabilities.Contains(capability);

    private async Task<bool> LoadAsync(Guid id, CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return false;
        ClientConfigured = true;
        var list = await _bots.ListAsync(client.Id, ct);
        Bot = list.Bots.FirstOrDefault(b => b.Id == id);
        if (Bot is null) return false;
        PlanIncludesAi = list.PlanIncludesAi;
        KnowledgeChoices = await _bots.ListKnowledgeChoicesAsync(client.Id, ct);
        NotReadyKey = BotRules.NotReadyKey(Bot);
        return true;
    }

    private void Fill(BotDto bot)
    {
        Name = bot.Name;
        Description = bot.Description;
        Instructions = bot.Instructions;
        Capabilities = bot.Capabilities.ToList();
        KnowledgeScope = bot.KnowledgeScope;
        KnowledgeDocumentIds = bot.KnowledgeDocumentIds.ToList();
        WebsiteSourceIds = bot.WebsiteSourceIds.ToList();
        Language = bot.Language;
        HandoffPolicy = bot.HandoffPolicy;
        HandoffMessage = bot.HandoffMessage;
        Status = bot.Status;
    }
}
