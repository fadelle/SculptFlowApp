using Omni.App.Controllers.Filters;
using Omni.App.Common.Enums;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.WhatsApp;
using Omni.App.Entities.Responses.WhatsApp;

namespace Omni.App.Pages.WhatsApp.Health;

/// <summary>WhatsApp account/phone-number health — a simplified, client-facing view over
/// ChannelIntegration's health fields (populated by Meta webhooks via n8n). See WhatsAppHealthService.</summary>
[RequireConnectedChannel(ChannelType.WhatsApp)]
public class IndexModel : PageModel
{
    private readonly ICurrentClientContext _clientContext;
    private readonly IWhatsAppHealthService _health;

    public IndexModel(ICurrentClientContext clientContext, IWhatsAppHealthService health)
    {
        _clientContext = clientContext;
        _health = health;
    }

    public bool ClientConfigured { get; private set; }
    public Guid ClientId { get; private set; }
    public WhatsAppHealthResponse? Health { get; private set; }
    public IReadOnlyList<WhatsAppHealthEventResponse> Events { get; private set; } = Array.Empty<WhatsAppHealthEventResponse>();

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
        Health = await _health.GetHealthAsync(client.Id, ct);
        Events = await _health.GetHealthEventsAsync(client.Id, skip: 0, take: 25, ct);
    }
}
