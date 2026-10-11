using PlasticSurgery.Entities.Dtos.Voice;

namespace PlasticSurgery.Business.Contracts.Services.Inbox;

/// <summary>Playback of a message's media for the Inbox. Voice notes only for now.</summary>
public interface IMessageMediaService
{
    /// <summary>The audio of a voice note, fetched from its channel on demand (nothing is stored). KeyNotFoundException when the
    /// message isn't this clinic's or its audio is no longer available; ArgumentException when the message isn't audio.</summary>
    Task<VoiceAudio> GetAudioAsync(Guid clinicId, Guid messageId, CancellationToken ct = default);
}
