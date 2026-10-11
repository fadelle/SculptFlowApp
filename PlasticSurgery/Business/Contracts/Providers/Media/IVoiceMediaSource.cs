using PlasticSurgery.Entities.Dtos.Voice;
using PlasticSurgery.Entities.Models;

namespace PlasticSurgery.Business.Contracts.Providers.Media;

/// <summary>
/// One adapter per messaging channel that can hand over the audio of a voice note, resolved by Message.Channel (the same
/// pattern as IChannelSender, in the other direction). It is the only place that knows how a channel stores media and which
/// token opens it; callers (the Inbox player today, the n8n voice send later) just ask for the audio.
/// </summary>
public interface IVoiceMediaSource
{
    /// <summary>The ConversationChannel value this source handles.</summary>
    string Channel { get; }

    /// <summary>Downloads the audio of an inbound voice/audio message of this channel. Null when the message carries nothing
    /// to download (no file reference). Throws when the channel is not connected, the file is over the size limit or the
    /// provider refuses; the message never contains a token.</summary>
    Task<VoiceAudio?> DownloadAsync(Message message, CancellationToken ct = default);
}
