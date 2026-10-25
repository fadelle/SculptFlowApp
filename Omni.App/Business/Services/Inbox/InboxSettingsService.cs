using Omni.App.Business.Contracts.Providers.Channels;
using Omni.App.Business.Contracts.Services.Billing;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Common.Enums;
using Omni.App.Common.Exceptions;
using Omni.App.Common.Helpers;
using Omni.App.Common.Statics;
using Omni.App.Entities.Dtos.Billing;
using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Dtos.Ui;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Ai;
using Omni.App.Persistence.Contracts.Channels;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Inbox;

namespace Omni.App.Business.Services.Inbox;

public class InboxSettingsService : IInboxSettingsService
{
    private readonly IInboxSettingsRepository _settings;
    private readonly IBotRepository _bots;
    private readonly IClientRepository _clients;
    private readonly IEntitlementService _entitlements;
    private readonly IChannelIntegrationRepository _channels;
    private readonly IReadOnlySet<string> _botChannels;
    private readonly IUnitOfWork _unitOfWork;

    public InboxSettingsService(IInboxSettingsRepository settings, IBotRepository bots, IClientRepository clients, IEntitlementService entitlements,
        IChannelIntegrationRepository channels, IEnumerable<IChannelSender> senders, IUnitOfWork unitOfWork)
    {
        // A bot answers only where Omni can send its reply (WhatsApp, Telegram): other connections (Instagram) have no AI settings.
        _botChannels = senders.Select(s => s.Channel).ToHashSet(StringComparer.Ordinal);
        _settings = settings;
        _bots = bots;
        _clients = clients;
        _entitlements = entitlements;
        _channels = channels;
        _unitOfWork = unitOfWork;
    }

    public async Task<InboxSettingsDto> GetAsync(Guid clientId, CancellationToken ct = default)
    {
        _ = await _clients.GetReadOnlyAsync(clientId, ct) ?? throw new KeyNotFoundException("clients.errors.notFound");
        var facts = await LoadAsync(clientId, ct);
        var accounts = facts.Accounts
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Channel, StringComparer.Ordinal)
            .Select(account =>
            {
                var row = facts.RowFor(account.Id);
                var bot = row?.BotId is { } botId ? facts.Bots.FirstOrDefault(b => b.Id == botId) : null;
                return new AccountAiSettingsDto(account.Id, account.Channel, account.DisplayName,
                    row?.AiEnabled ?? false, bot?.Id, row?.AiBotNewConversations ?? true, row?.AiBotReopenedConversations ?? true,
                    bot?.Name, bot?.Status, facts.Decide(account, row).BlockReason);
            })
            .ToList();
        var choices = facts.Bots.Select(b => new BotChoiceDto(b.Id, b.Name, b.Status)).ToList();
        return new InboxSettingsDto(facts.Entitlements.CanUseAiAssistant, facts.Entitlements.MaximumAiBots, accounts, choices);
    }

    public async Task<AccountAiDecision> ResolveAsync(Guid clientId, string channel, CancellationToken ct = default)
    {
        // Every read is cached (channel connections, Inbox settings, bots, entitlements), so this is cheap per message.
        var facts = await LoadAsync(clientId, ct);
        var account = facts.Accounts.FirstOrDefault(a => a.Channel == channel);
        return account is null
            ? new AccountAiDecision(null, null, facts.Entitlements.CanUseAiAssistant ? AiBotBlockReason.NoAccount : AiBotBlockReason.PlanWithoutAi, false, false)
            : facts.Decide(account, facts.RowFor(account.Id));
    }

    public async Task<string> InitialModeAsync(Guid clientId, string channel, CancellationToken ct = default) =>
        (await ResolveAsync(clientId, channel, ct)).AnswersNew ? ConversationMode.Ai : ConversationMode.Human;

    public async Task UpdateAccountAsync(Guid clientId, Guid channelIntegrationId, UpdateAccountAiRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = await _clients.GetReadOnlyAsync(clientId, ct) ?? throw new KeyNotFoundException("clients.errors.notFound");
        var account = (await _channels.ListForClientReadOnlyAsync(clientId, ct))
            .FirstOrDefault(c => c.Id == channelIntegrationId && c.Status == ChannelIntegrationStatus.Connected && _botChannels.Contains(c.Channel))
            ?? throw new KeyNotFoundException("inboxSettings.errors.accountNotFound");

        Bot? bot = null;
        if (request.BotId is { } botId)
        {
            bot = (await _bots.ListForClientReadOnlyAsync(clientId, ct)).FirstOrDefault(b => b.Id == botId)
                ?? throw new ArgumentException("inboxSettings.errors.botNotFound");
        }
        if (request.AiEnabled)
        {
            await _entitlements.EnsureFeatureAsync(clientId, EntitlementKeys.AiAssistant, ct);
            if (bot is null) throw new ArgumentException("inboxSettings.errors.botRequired");
            if (bot.Status != BotStatus.Active) throw new ArgumentException("inboxSettings.errors.botNotActive").WithArgs(TextArg.Value(bot.Name));
        }

        var now = DateTimeOffset.UtcNow;
        var row = await _settings.GetForAccountAsync(clientId, account.Id, ct);
        if (row is null)
        {
            row = new InboxSettings { Id = Guid.NewGuid(), ClientId = clientId, ChannelIntegrationId = account.Id, CreatedAt = now };
            _settings.Add(row);
        }
        row.AiEnabled = request.AiEnabled;
        row.BotId = bot?.Id;
        row.AiBotNewConversations = request.AiBotNewConversations;
        row.AiBotReopenedConversations = request.AiBotReopenedConversations;
        row.UpdatedAt = now;
        // Tracked save: CacheInvalidationInterceptor drops the client's cached settings. Two first saves racing for the same
        // account hit the unique index; the second surfaces as DuplicateRecordException (409).
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<Facts> LoadAsync(Guid clientId, CancellationToken ct)
    {
        var entitlements = await _entitlements.GetAsync(clientId, ct);
        var accounts = (await _channels.ListForClientReadOnlyAsync(clientId, ct))
            .Where(c => c.Status == ChannelIntegrationStatus.Connected && _botChannels.Contains(c.Channel)).ToList();
        var rows = await _settings.ListForClientReadOnlyAsync(clientId, ct);
        var bots = await _bots.ListForClientReadOnlyAsync(clientId, ct);
        return new Facts(entitlements, accounts, rows, bots, BotRules.AnsweringBotIds(bots, entitlements.MaximumAiBots));
    }

    /// <summary>Everything one decision reads, loaded once.</summary>
    private sealed record Facts(ClientEntitlements Entitlements, IReadOnlyList<ChannelIntegration> Accounts, IReadOnlyList<InboxSettings> Rows,
        IReadOnlyList<Bot> Bots, IReadOnlySet<Guid> AnsweringBotIds)
    {
        public InboxSettings? RowFor(Guid accountId) => Rows.FirstOrDefault(r => r.ChannelIntegrationId == accountId);

        public AccountAiDecision Decide(ChannelIntegration account, InboxSettings? row)
        {
            var bot = row?.BotId is { } botId ? Bots.FirstOrDefault(b => b.Id == botId) : null;
            var reason = !Entitlements.CanUseAiAssistant ? AiBotBlockReason.PlanWithoutAi
                : row is not { AiEnabled: true } ? AiBotBlockReason.AiDisabled
                : bot is null ? AiBotBlockReason.NoBot
                : bot.Status != BotStatus.Active ? AiBotBlockReason.BotInactive
                : !AnsweringBotIds.Contains(bot.Id) ? AiBotBlockReason.BotLimit
                : null;
            return new AccountAiDecision(account.Id, bot?.Id, reason, row?.AiBotNewConversations ?? true, row?.AiBotReopenedConversations ?? true);
        }
    }
}
