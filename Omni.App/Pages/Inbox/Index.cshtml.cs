using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Entities.Dtos.Inbox;

namespace Omni.App.Pages.Inbox;

/// <summary>
/// The Inbox shell page. Server-renders the first page of conversations for a fast initial paint;
/// everything after that (selecting a conversation, loading its messages, sending, Take Over/
/// Return to AI/Close, and live updates) is handled client-side by wwwroot/js/inbox.js against the
/// REST API + the InboxHub SignalR connection — see that file's header comment for why.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IConversationService _conversations;
    private readonly IInboxSettingsService _inboxSettings;

    public IndexModel(ICurrentClientContext clientContext, IConversationService conversations, IInboxSettingsService inboxSettings)
    {
        _inboxSettings = inboxSettings;
        _clientContext = clientContext;
        _conversations = conversations;
    }

    public bool ClientConfigured { get; private set; }
    public Guid ClientId { get; private set; }
    public IReadOnlyList<ConversationListRow> Conversations { get; private set; } = Array.Empty<ConversationListRow>();

    /// <summary>The plan includes the AI agent; without it inbox.js hides Return to AI (the API refuses it too).</summary>
    public bool AiAgentAvailable { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var client = await _clientContext.GetClientAsync(ct);
        if (client is null)
        {
            ClientConfigured = false;
            return;
        }

        ClientConfigured = true;
        ClientId = client.Id;
        AiAgentAvailable = (await _inboxSettings.GetAsync(client.Id, ct)).PlanIncludesAiBot;

        var (items, _) = await _conversations.ListAsync(client.Id, skip: 0, take: 50, ct);
        Conversations = items;
    }
}
