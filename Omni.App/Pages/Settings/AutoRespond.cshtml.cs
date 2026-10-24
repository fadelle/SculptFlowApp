using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Requests.Inbox;

namespace Omni.App.Pages.Settings;

/// <summary>Settings → Auto-respond: per channel, the automatic reply to a contact's first message in a conversation and to a
/// message that reopens a closed one (IAutoReplyService). One form per channel; any member may change them.</summary>
public class AutoRespondModel : PageModel
{
    private const string SavedKey = "autoReply.saved";

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

    [BindProperty] public string Channel { get; set; } = "";
    [BindProperty] public bool NewConversationEnabled { get; set; }
    [BindProperty] public string? NewConversationMessage { get; set; }
    [BindProperty] public string ReopenedMode { get; set; } = AutoReplyReopenedMode.Off;
    [BindProperty] public string? ReopenedMessage { get; set; }
    [BindProperty] public bool SendWhenAiAnswers { get; set; }

    [TempData] public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>The channel whose form failed to save: it shows what was posted instead of the saved values.</summary>
    public string? FailedChannel { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var clientId = await LoadAsync(ct);
        if (clientId is null) return RedirectToPage();
        try
        {
            await _autoReplies.SaveAsync(clientId.Value, new SaveAutoReplyRequest(Channel, NewConversationEnabled, NewConversationMessage,
                ReopenedMode, ReopenedMessage, SendWhenAiAnswers), ct);
            StatusMessage = _text[SavedKey, ChannelNames.Brand(Channel)].Value;
            return RedirectToPage();
        }
        catch (ArgumentException ex)
        {
            ErrorMessage = _text.Localize(ex);
            FailedChannel = Channel;
            return Page();
        }
    }

    /// <summary>What a channel's form shows: the posted values after a failed save, else the saved settings.</summary>
    public AutoReplyDto FormValues(AutoReplyChannelDto channel) => FailedChannel == channel.Settings.Channel
        ? new AutoReplyDto(Channel, NewConversationEnabled, NewConversationMessage, ReopenedMode, ReopenedMessage, SendWhenAiAnswers)
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
