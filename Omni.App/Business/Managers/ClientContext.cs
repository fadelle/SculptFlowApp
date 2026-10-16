using Omni.App.Business.Contracts.Managers;
using Omni.App.Entities.Models;
using Omni.App.Persistence.Contracts.Clients;

namespace Omni.App.Business.Managers;

public class ClientContext : IClientContext
{
    private readonly IClientRepository _clients;

    public ClientContext(IClientRepository clients)
    {
        _clients = clients;
    }

    public Task<Client?> GetByIdAsync(Guid clientId, CancellationToken ct = default) => _clients.GetReadOnlyAsync(clientId, ct);
}
