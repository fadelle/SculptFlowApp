using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Common.Enums;
using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Inbox;

namespace Omni.App.Business.Services.Inbox;

public class InboxSettingsService : IInboxSettingsService
{
    private readonly IInboxSettingsRepository _settings;
    private readonly IClientRepository _clients;
    private readonly IEntitlementService _entitlements;
    private readonly IUnitOfWork _unitOfWork;

    public InboxSettingsService(IInboxSettingsRepository settings, IClientRepository clients, IEntitlementService entitlements,
        IUnitOfWork unitOfWork)
    {
        _settings = settings;
        _clients = clients;
        _entitlements = entitlements;
        _unitOfWork = unitOfWork;
    }

    public async Task<InboxSettingsDto> GetAsync(Guid clientId, CancellationToken ct = default)
    {
        // All three reads are cached (inbox settings and client table caches, entitlements), so this is cheap per message.
        _ = await _clients.GetReadOnlyAsync(clientId, ct) ?? throw new KeyNotFoundException("Client not found.");
        var row = await _settings.GetForClientReadOnlyAsync(clientId, ct) ?? new InboxSettings();
        var entitlements = await _entitlements.GetAsync(clientId, ct);
        return new InboxSettingsDto(entitlements.CanUseAiAgent, row.AiBotNewConversations, row.AiBotReopenedConversations);
    }

    public async Task<string> InitialModeAsync(Guid clientId, CancellationToken ct = default) =>
        (await GetAsync(clientId, ct)).AiBotAnswersNew ? ConversationMode.Ai : ConversationMode.Human;

    public async Task UpdateAsync(Guid clientId, UpdateInboxSettingsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = await _clients.GetReadOnlyAsync(clientId, ct) ?? throw new KeyNotFoundException("Client not found.");

        var now = DateTimeOffset.UtcNow;
        var row = await _settings.GetForClientAsync(clientId, ct);
        if (row is null)
        {
            row = new InboxSettings { Id = Guid.NewGuid(), ClientId = clientId, CreatedAt = now };
            _settings.Add(row);
        }
        else if (row.AiBotNewConversations == request.AiBotNewConversations
                 && row.AiBotReopenedConversations == request.AiBotReopenedConversations)
        {
            return;
        }

        row.AiBotNewConversations = request.AiBotNewConversations;
        row.AiBotReopenedConversations = request.AiBotReopenedConversations;
        row.UpdatedAt = now;
        // Tracked save: CacheInvalidationInterceptor drops the client's cached settings. Two first saves racing for the
        // same client hit the unique index; the second surfaces as DuplicateRecordException (409).
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
