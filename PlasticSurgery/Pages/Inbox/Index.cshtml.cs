using Microsoft.AspNetCore.Mvc.RazorPages;
using PlasticSurgery.Dtos;
using PlasticSurgery.Services;

namespace PlasticSurgery.Pages.Inbox;

/// <summary>
/// The Inbox shell page. Server-renders the first page of conversations for a fast initial paint;
/// everything after that (selecting a conversation, loading its messages, sending, Take Over/
/// Return to AI/Close, and live updates) is handled client-side by wwwroot/js/inbox.js against the
/// REST API + the InboxHub SignalR connection — see that file's header comment for why.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ICurrentClinicContext _clinicContext;
    private readonly IConversationService _conversations;

    public IndexModel(ICurrentClinicContext clinicContext, IConversationService conversations)
    {
        _clinicContext = clinicContext;
        _conversations = conversations;
    }

    public bool ClinicConfigured { get; private set; }
    public Guid ClinicId { get; private set; }
    public IReadOnlyList<ConversationListRow> Conversations { get; private set; } = Array.Empty<ConversationListRow>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        var clinic = await _clinicContext.GetClinicAsync(ct);
        if (clinic is null)
        {
            ClinicConfigured = false;
            return;
        }

        ClinicConfigured = true;
        ClinicId = clinic.Id;

        var (items, _) = await _conversations.ListAsync(clinic.Id, skip: 0, take: 50, ct);
        Conversations = items;
    }
}
