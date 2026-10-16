using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Services.Automation;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Clients;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Users;

namespace Omni.App.Business.Services.Automation;

public class AutomationCleanupService : IAutomationCleanupService
{
    private readonly IClientRepository _clients;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheManager _cache;

    public AutomationCleanupService(IClientRepository clients, IUserRepository users, IUnitOfWork unitOfWork, ICacheManager cache)
    {
        _clients = clients;
        _users = users;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public Task<IReadOnlyList<ClientSummaryRow>> ListClientsAsync(CancellationToken ct = default) =>
        _clients.ListByNamePrefixAsync(AutomationClients.NamePrefix, ct);

    public async Task<AutomationDeleteOutcome> DeleteClientAsync(Guid clientId, CancellationToken ct = default)
    {
        var client = await _clients.GetReadOnlyAsync(clientId, ct);
        if (client is null) return AutomationDeleteOutcome.NotFound;

        var userIds = await _clients.ListMemberUserIdsAsync(clientId, ct);
        var emails = await _users.ListEmailsAsync(userIds, ct);

        var isAutomationClient = client.Name.StartsWith(AutomationClients.NamePrefix, StringComparison.Ordinal)
            && emails.Count > 0
            && emails.All(e => e.EndsWith(AutomationClients.EmailDomain, StringComparison.OrdinalIgnoreCase));
        if (!isAutomationClient) return AutomationDeleteOutcome.NotAutomationClient;

        await using var tx = await _unitOfWork.BeginTransactionAsync(ct);
        await _clients.DeleteAsync(clientId, ct);
        await _users.DeleteAsync(userIds, ct);
        await tx.CommitAsync(ct);
        await _cache.RemoveByPrefixAsync(CacheKeys.WhatsAppRoutingPrefix, ct);
        await _cache.RemoveAsync(CacheKeys.Entitlements(clientId), ct);
        return AutomationDeleteOutcome.Deleted;
    }
}
