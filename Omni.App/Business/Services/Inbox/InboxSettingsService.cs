using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Common.Enums;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Channels;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Inbox;

namespace Omni.App.Business.Services.Inbox;

public class InboxSettingsService : IInboxSettingsService
{
    private readonly IInboxSettingsRepository _settings;
    private readonly IClientRepository _clients;
    private readonly IEntitlementService _entitlements;
    private readonly IChannelIntegrationRepository _channels;
    private readonly IUnitOfWork _unitOfWork;

    public InboxSettingsService(IInboxSettingsRepository settings, IClientRepository clients, IEntitlementService entitlements,
        IChannelIntegrationRepository channels, IUnitOfWork unitOfWork)
    {
        _settings = settings;
        _channels = channels;
        _clients = clients;
        _entitlements = entitlements;
        _unitOfWork = unitOfWork;
    }

    public async Task<InboxSettingsDto> GetAsync(Guid clientId, CancellationToken ct = default)
    {
        // Every read is cached (inbox settings, client and channel table caches, entitlements), so this is cheap per message.
        _ = await _clients.GetReadOnlyAsync(clientId, ct) ?? throw new KeyNotFoundException("clients.errors.notFound");
        var row = await _settings.GetForClientReadOnlyAsync(clientId, ct) ?? new InboxSettings();
        var entitlements = await _entitlements.GetAsync(clientId, ct);

        // AI bots: the first max_ai_bots connected channels (in connection order) get the bot. A plan without the key (made
        // before Milestone C) sets no AI bot limit, so older plans keep the bot on every channel.
        IReadOnlySet<string>? withoutBot = null;
        int? botLimit = null;
        if (entitlements.CanUseAiAssistant && entitlements.Values.ContainsKey(EntitlementKeys.MaxAiBots)
            && entitlements.MaximumAiBots is int limit)
        {
            botLimit = limit;
            var connected = (await _channels.ListForClientReadOnlyAsync(clientId, ct))
                .Where(c => c.Status == ChannelIntegrationStatus.Connected)
                .OrderBy(c => c.CreatedAt).ThenBy(c => c.Channel, StringComparer.Ordinal)
                .Select(c => c.Channel)
                .ToList();
            withoutBot = connected.Skip(Math.Max(limit, 0)).ToHashSet(StringComparer.Ordinal);
        }
        return new InboxSettingsDto(entitlements.CanUseAiAssistant, row.AiBotNewConversations, row.AiBotReopenedConversations,
            withoutBot, botLimit);
    }

    public async Task<string> InitialModeAsync(Guid clientId, CancellationToken ct = default) =>
        (await GetAsync(clientId, ct)).AiBotAnswersNew ? ConversationMode.Ai : ConversationMode.Human;

    public async Task<string> InitialModeAsync(Guid clientId, string channel, CancellationToken ct = default)
    {
        var settings = await GetAsync(clientId, ct);
        return settings.AiBotAnswersNew && settings.AiBotAnswersOn(channel) ? ConversationMode.Ai : ConversationMode.Human;
    }

    public async Task UpdateAsync(Guid clientId, UpdateInboxSettingsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = await _clients.GetReadOnlyAsync(clientId, ct) ?? throw new KeyNotFoundException("clients.errors.notFound");

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
