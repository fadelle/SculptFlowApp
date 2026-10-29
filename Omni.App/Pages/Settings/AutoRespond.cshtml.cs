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

/// <summary>Settings → Auto-respond: per channel, the automatic reply to a contact's first message in a conversation and to a
/// message that reopens a closed one (IAutoReplyService). One form holds every channel, a tab each (?tab= picks the first
/// one shown); one Save stores them all, all or nothing (SaveAllAsync). Any member may change them.</summary>
public class AutoRespondModel : PageModel
{
    private const string SavedKey = "autoReply.savedAll";

    private readonly ICurrentClientContext _clientContext;
    private readonly IAutoReplyService _autoReplies;
    private readonly IConfigManager _config;
    private readonly IStringLocalizer _text;

    public AutoRespondModel(ICurrentClientContext clientContext, IAutoReplyService autoReplies, IConfigManager config, IStringLocalizer text)
    {
        _clientContext = clientContext;
        _autoReplies = autoReplies;
        _config = config;
        _text = text;
    }

    public bool ClientConfigured { get; private set; }
    public IReadOnlyList<AutoReplyChannelDto> Channels { get; private set; } = [];
    public int MaxLength => _config.AutoReplyMaxLength;
    public bool DisabledByAdmin => !_config.AutoReplyEnabled;

    /// <summary>Every channel's fields, posted as Replies[i].*.</summary>
    [BindProperty] public List<AutoReplyFormRow> Replies { get; set; } = [];

    [TempData] public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>?tab={channel}: the channel shown first.</summary>
    [BindProperty(SupportsGet = true)] public string? Tab { get; set; }

    /// <summary>The channel shown first: the one whose save failed, else the asked tab, else the first connected one.</summary>
    public string? ActiveChannel =>
        (Channels.FirstOrDefault(c => c.Settings.Channel == (FailedChannel ?? Tab))
         ?? Channels.FirstOrDefault(c => c.Connected) ?? Channels.FirstOrDefault())?.Settings.Channel;

    /// <summary>The channel a failed save stopped at; after a failed save every tab shows what was posted.</summary>
    public string? FailedChannel { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var clientId = await LoadAsync(ct);
        if (clientId is null) return RedirectToPage();
        try
        {
            await _autoReplies.SaveAllAsync(clientId.Value, Replies.Select(r => r.ToRequest()).ToList(), ct);
            StatusMessage = _text[SavedKey].Value;
            return RedirectToPage(new { tab = Tab });
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = _text.Localize(ex);
            FailedChannel = (ex as AutoReplyInvalidException)?.Channel ?? Tab;
            return Page();
        }
    }

    /// <summary>What a channel's fields show: what was posted after a failed save, else the saved settings.</summary>
    public AutoReplyDto FormValues(AutoReplyChannelDto channel) =>
        ErrorMessage is not null && Replies.FirstOrDefault(r => r.Channel == channel.Settings.Channel) is { } posted
            ? new AutoReplyDto(posted.Channel, posted.NewConversationEnabled, posted.NewConversationMessage, posted.ReopenedMode,
                posted.ReopenedMessage, posted.SendWhenAiAnswers)
            : channel.Settings;

    private async Task<Guid?> LoadAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null) return null;
        ClientConfigured = true;
        Channels = await _autoReplies.ListAsync(client.Id, ct);
        return client.Id;
    }
}
