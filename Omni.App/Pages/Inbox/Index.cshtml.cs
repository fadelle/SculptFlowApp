using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.BusinessTypes;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.BusinessTypes;
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
    private readonly IBusinessTypeService _businessTypes;

    public IndexModel(ICurrentClientContext clientContext, IConversationService conversations, IInboxSettingsService inboxSettings,
        IBusinessTypeService businessTypes)
    {
        _businessTypes = businessTypes;
        _inboxSettings = inboxSettings;
        _clientContext = clientContext;
        _conversations = conversations;
    }

    public bool ClientConfigured { get; private set; }
    public Guid ClientId { get; private set; }
    public IReadOnlyList<ConversationListRow> Conversations { get; private set; } = Array.Empty<ConversationListRow>();

    /// <summary>The plan includes the AI agent; without it inbox.js hides Return to AI (the API refuses it too).</summary>
    public bool AiAgentAvailable { get; private set; }

    /// <summary>The client's pipeline (contact panel: stage badge and journey).</summary>
    public ClientPipeline Pipeline { get; private set; } = ClientPipeline.Empty;

    /// <summary>The journey steps the contact panel shows, one per category, in order.</summary>
    public static readonly IReadOnlyList<string> JourneyCategories =
        [StageCategory.Open, StageCategory.Booked, StageCategory.Attended, StageCategory.Converted];

    /// <summary>A journey step's label: the client's first stage of that category.</summary>
    public string JourneyLabel(string category) => Pipeline.FirstOf(category)?.Label ?? category;

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
        Pipeline = await _businessTypes.GetPipelineAsync(client.Id, ct);

        var (items, _) = await _conversations.ListAsync(client.Id, skip: 0, take: 50, ct);
        Conversations = items;
    }
}
