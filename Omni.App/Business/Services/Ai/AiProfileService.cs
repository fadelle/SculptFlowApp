using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Ai;
using Omni.App.Entities.Dtos.Ai;
using Omni.App.Persistence.Contracts.BusinessTypes;
using Omni.App.Persistence.Contracts.Clients;

namespace Omni.App.Business.Services.Ai;

public class AiProfileService : IAiProfileService
{
    private readonly IClientRepository _clients;
    private readonly IBusinessTypeRepository _types;
    private readonly ITerminology _terminology;

    public AiProfileService(IClientRepository clients, IBusinessTypeRepository types, ITerminology terminology)
    {
        _clients = clients;
        _types = types;
        _terminology = terminology;
    }

    public async Task<AiClientProfile?> GetForClientAsync(Guid clientId, CancellationToken ct = default)
    {
        var client = await _clients.GetReadOnlyAsync(clientId, ct);
        if (client is null) return null;

        var type = await _types.GetReadOnlyAsync(client.BusinessTypeCode, ct);
        var terms = await _terminology.ForBusinessTypeAsync(client.BusinessTypeCode, client.DefaultLanguage, ct);
        return new AiClientProfile(client.Name, client.BusinessTypeCode, client.DefaultLanguage, client.Timezone,
            type?.AiProfile ?? string.Empty, terms);
    }
}
