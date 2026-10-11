namespace PlasticSurgery.Entities.Dtos.Voice;

/// <summary>A voice note's audio as downloaded from its channel: the bytes plus what a player or a transcriber needs to
/// open them. Provider specifics (file ids, tokens) never appear here.</summary>
public record VoiceAudio(byte[] Bytes, string ContentType, string FileName, int? DurationSeconds);
