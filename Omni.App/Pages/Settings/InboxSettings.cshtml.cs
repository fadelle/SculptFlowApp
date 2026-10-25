using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Requests.Inbox;

namespace Omni.App.Pages.Settings;

/// <summary>Settings → Inbox settings: per connected account (each WhatsApp number, Telegram bot, ...), whether AI answers, which
/// bot, and whether it takes new and reopened conversations (IInboxSettingsService). One form per account; saving one never
/// changes another. Bots themselves are made on the Bots page.</summary>
public class InboxSettingsModel : PageModel
{
    private const string SavedKey = "inboxSettings.saved";

    private readonly ICurrentClientContext _clientContext;
    private readonly IInboxSettingsService _inboxSettings;
    private readonly IStringLocalizer _text;

    public InboxSettingsModel(ICurrentClientContext clientContext, IInboxSettingsService inboxSettings, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _inboxSettings = inboxSettings;
        _text = text;
    }

    public bool ClientConfigured { get; private set; }
    public InboxSettingsDto? Settings { get; private set; }

    [BindProperty] public Guid ChannelIntegrationId { get; set; }
    [BindProperty] public bool AiEnabled { get; set; }
    [BindProperty] public Guid? BotId { get; set; }
    [BindProperty] public bool AiBotNewConversations { get; set; }
    [BindProperty] public bool AiBotReopenedConversations { get; set; }

    [TempData] public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>The account whose form failed to save: it shows what was posted instead of the saved values.</summary>
    public Guid? FailedAccount { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var clientId = await LoadAsync(ct);
        if (clientId is null) return RedirectToPage();
        try
        {
            await _inboxSettings.UpdateAccountAsync(clientId.Value, ChannelIntegrationId,
                new UpdateAccountAiRequest(AiEnabled, BotId, AiBotNewConversations, AiBotReopenedConversations), ct);
            var account = Settings!.Accounts.FirstOrDefault(a => a.ChannelIntegrationId == ChannelIntegrationId);
            StatusMessage = _text[SavedKey, AccountName(account)].Value;
            return RedirectToPage();
        }
        catch (KeyNotFoundException)
        {
            return RedirectToPage();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // InvalidOperationException: EntitlementDeniedException (the plan has no AI assistant).
            ErrorMessage = _text.Localize(ex);
            FailedAccount = ChannelIntegrationId;
            return Page();
        }
    }

    /// <summary>What an account's form shows: the posted values after a failed save, else the saved settings.</summary>
    public AccountAiSettingsDto FormValues(AccountAiSettingsDto account) => FailedAccount == account.ChannelIntegrationId
        ? account with { AiEnabled = AiEnabled, BotId = BotId, AiBotNewConversations = AiBotNewConversations, AiBotReopenedConversations = AiBotReopenedConversations }
        : account;

    /// <summary>"WhatsApp · +961 70 000 000": the channel's brand and the account's own name when it has one.</summary>
    public static string AccountName(AccountAiSettingsDto? account) => account is null ? ""
        : string.IsNullOrWhiteSpace(account.DisplayName) ? ChannelNames.Brand(account.Channel)
        : $"{ChannelNames.Brand(account.Channel)} · {account.DisplayName}";

    private async Task<Guid?> LoadAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return null;
        ClientConfigured = true;
        Settings = await _inboxSettings.GetAsync(client.Id, ct);
        return client.Id;
    }
}
