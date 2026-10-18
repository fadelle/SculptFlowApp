using Omni.App.Business.Contracts.Providers.Media;
using Omni.App.Business.Contracts.Services.Inbox;
using Omni.App.Entities.Dtos.Voice;
using Omni.App.Persistence.Contracts.Inbox;
using Omni.App.Common.Helpers;

namespace Omni.App.Business.Services.Inbox;

/// <summary>
/// Plays a voice note in the Inbox: finds the message in the caller's client, then asks the channel's own
/// <see cref="IVoiceMediaSource"/> for the audio. Nothing is stored — the file is fetched from the provider each time (the
/// browser caches the response), so there is no copy of a contact's recording to protect or expire.
/// </summary>
public class MessageMediaService : IMessageMediaService
{
    private const string AudioMessageType = "audio";

    private readonly IMessageRepository _messages;
    private readonly IEnumerable<IVoiceMediaSource> _sources;

    public MessageMediaService(IMessageRepository messages, IEnumerable<IVoiceMediaSource> sources)
    {
        _messages = messages;
        _sources = sources;
    }

    public async Task<VoiceAudio> GetAudioAsync(Guid clientId, Guid messageId, CancellationToken ct = default)
    {
        var message = await _messages.GetForClientReadOnlyAsync(clientId, messageId, ct)
            ?? throw new KeyNotFoundException("inbox.errors.messageNotFound");

        if (!string.Equals(message.MessageType, AudioMessageType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("inbox.errors.noAudio");
        }

        var source = _sources.FirstOrDefault(s => s.Channel == message.Channel)
            ?? throw new InvalidOperationException("inbox.errors.voiceUnsupported").WithArgs(message.Channel);

        return await source.DownloadAsync(message, ct)
            ?? throw new KeyNotFoundException("inbox.errors.audioGone");
    }
}
