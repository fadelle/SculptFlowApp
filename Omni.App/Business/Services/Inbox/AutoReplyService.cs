using Omni.App.Business.Contracts.Managers;
using Omni.App.Business.Contracts.Providers.Channels;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Common.Enums;
using Omni.App.Common.Helpers;
using Omni.App.Entities.Dtos.Inbox;
using Omni.App.Entities.Models;
using Omni.App.Entities.Requests.Inbox;
using Omni.App.Persistence.Contracts;
using Omni.App.Persistence.Contracts.Channels;
using Omni.App.Persistence.Contracts.Clients;
using Omni.App.Persistence.Contracts.Inbox;

namespace Omni.App.Business.Services.Inbox;

public class AutoReplyService : IAutoReplyService
{
    private readonly IAutoReplyRepository _autoReplies;
    private readonly IClientRepository _clients;
    private readonly IChannelIntegrationRepository _channels;
    private readonly IInboxSettingsService _inboxSettings;
    private readonly IEnumerable<IChannelSender> _senders;
    private readonly IConfigManager _config;
    private readonly IUnitOfWork _unitOfWork;

    public AutoReplyService(IAutoReplyRepository autoReplies, IClientRepository clients, IChannelIntegrationRepository channels,
        IInboxSettingsService inboxSettings, IEnumerable<IChannelSender> senders, IConfigManager config, IUnitOfWork unitOfWork)
    {
        _autoReplies = autoReplies;
        _clients = clients;
        _channels = channels;
        _inboxSettings = inboxSettings;
        _senders = senders;
        _config = config;
        _unitOfWork = unitOfWork;
    }

    /// <summary>Channels with an outbound sender (an automatic reply is plain text), in registration order.</summary>
    private IReadOnlyList<string> SendableChannels() => _senders.Select(s => s.Channel).Distinct(StringComparer.Ordinal).ToList();

    public async Task<IReadOnlyList<AutoReplyChannelDto>> ListAsync(Guid clientId, CancellationToken ct = default)
    {
        _ = await _clients.GetReadOnlyAsync(clientId, ct) ?? throw new KeyNotFoundException("clients.errors.notFound");
        var rows = (await _autoReplies.ListForClientReadOnlyAsync(clientId, ct)).ToDictionary(r => r.Channel, StringComparer.Ordinal);
        var connected = (await _channels.ListForClientReadOnlyAsync(clientId, ct))
            .Where(c => c.Status == ChannelIntegrationStatus.Connected).Select(c => c.Channel).ToHashSet(StringComparer.Ordinal);
        var inbox = await _inboxSettings.GetAsync(clientId, ct);

        return SendableChannels().Select(channel => new AutoReplyChannelDto(
            rows.TryGetValue(channel, out var row) ? ToDto(row) : Default(channel),
            connected.Contains(channel),
            inbox.AiBotAnswersNew && inbox.AiBotAnswersOn(channel),
            inbox.AiBotAnswersReopened && inbox.AiBotAnswersOn(channel))).ToList();
    }

    public async Task SaveAsync(Guid clientId, SaveAutoReplyRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!SendableChannels().Contains(request.Channel)) throw new ArgumentException("autoReply.errors.channel");
        if (!AutoReplyReopenedMode.All.Contains(request.ReopenedMode ?? "")) throw new ArgumentException("autoReply.errors.mode");
        var newMessage = Clean(request.NewConversationMessage);
        var reopenedMessage = Clean(request.ReopenedMessage);
        if (request.NewConversationEnabled && newMessage is null) throw new ArgumentException("autoReply.errors.newMessageRequired");
        if (request.ReopenedMode == AutoReplyReopenedMode.Same && newMessage is null)
            throw new ArgumentException("autoReply.errors.sameNeedsNewMessage");
        if (request.ReopenedMode == AutoReplyReopenedMode.Custom && reopenedMessage is null)
            throw new ArgumentException("autoReply.errors.reopenedMessageRequired");
        var max = _config.AutoReplyMaxLength;
        if ((newMessage?.Length ?? 0) > max || (reopenedMessage?.Length ?? 0) > max)
            throw new ArgumentException("autoReply.errors.tooLong").WithArgs(max);
        _ = await _clients.GetReadOnlyAsync(clientId, ct) ?? throw new KeyNotFoundException("clients.errors.notFound");

        var now = DateTimeOffset.UtcNow;
        var row = await _autoReplies.GetAsync(clientId, request.Channel, ct);
        if (row is null)
        {
            row = new AutoReply { Id = Guid.NewGuid(), ClientId = clientId, Channel = request.Channel, CreatedAt = now };
            _autoReplies.Add(row);
        }
        row.NewConversationEnabled = request.NewConversationEnabled;
        row.NewConversationMessage = newMessage;
        row.ReopenedMode = request.ReopenedMode!;
        row.ReopenedMessage = reopenedMessage;
        row.SendWhenAiAnswers = request.SendWhenAiAnswers;
        row.UpdatedAt = now;
        // Two first saves racing for the same channel hit the unique index; the second surfaces as DuplicateRecordException (409).
        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<AutoReplyChoice> ChooseAsync(Guid clientId, string channel, string trigger, bool aiAnswers, CancellationToken ct = default)
    {
        if (!_config.AutoReplyEnabled) return AutoReplyChoice.Skip(AutoReplyRules.SkipDisabledByAdmin);
        var row = await _autoReplies.GetReadOnlyAsync(clientId, channel, ct);
        return AutoReplyRules.Choose(row is null ? null : ToDto(row), trigger, aiAnswers);
    }

    private static string? Clean(string? message) => string.IsNullOrWhiteSpace(message) ? null : message.Trim();

    private static AutoReplyDto Default(string channel) => new(channel, false, null, AutoReplyReopenedMode.Off, null, false);

    private static AutoReplyDto ToDto(AutoReply row) => new(row.Channel, row.NewConversationEnabled, row.NewConversationMessage,
        row.ReopenedMode, row.ReopenedMessage, row.SendWhenAiAnswers);
}
